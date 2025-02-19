﻿using Diffusion.IO;
using Diffusion.Toolkit.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Diffusion.Database;

namespace Diffusion.Toolkit
{
    public partial class MainWindow
    {

        private async Task RescanTask()
        {
            if (_settings.ImagePaths.Any())
            {
                await Scan();
            }
            else
            {
                await _messagePopupManager.Show("No image paths configured!", "Rescan Folders");
            }
        }


        private async Task RebuildTask()
        {
            if (_settings.ImagePaths.Any())
            {
                var message = "This will update the metadata of ALL existing files in the database with current metadata in actual files.\r\n\r\n" +
                              "You only need to do this if you've updated the metadata in the files since they were added or if they contain metadata that an older version of this program didn't store.\r\n\r\n" +
                              "Are you sure you want to continue?";

                var result = await _messagePopupManager.ShowCustom(message, "Rebuild Metadata", PopupButtons.YesNo, 500, 400);
                if (result == PopupResult.Yes)
                {
                    await Rebuild();
                }
            }
            else
            {
                await _messagePopupManager.Show("No image paths configured!", "Rebuild Metadata");
            }
        }

        private async Task Scan()
        {

            _scanCancellationTokenSource = new CancellationTokenSource();

            await Task.Run(async () =>
            {
                var result = await ScanInternal(_settings!, false, true, _scanCancellationTokenSource.Token);
                if (result && _search != null)
                {
                    _search.SearchImages();
                }
            });
        }

        private async Task Rebuild()
        {
            _scanCancellationTokenSource = new CancellationTokenSource();

            await Task.Run(async () =>
            {
                var result = await ScanInternal(_settings!, true, true, _scanCancellationTokenSource.Token);
                if (result)
                {
                    _search.SearchImages();
                }
            });
        }

        private async Task MoveFiles(ICollection<ImageEntry> images, string path, bool remove)
        {

            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
            }

            Dispatcher.Invoke(() =>
            {
                _model.TotalFilesScan = images.Count;
                _model.CurrentPositionScan = 0;
            });

            var moved = 0;

            foreach (var image in images)
            {
                var fileName = Path.GetFileName(image.Path);
                var newPath = Path.Join(path, fileName);
                if (image.Path != newPath)
                {
                    File.Move(image.Path, newPath);

                    if (remove)
                    {
                        _dataStore.DeleteImage(image.Id);
                    }
                    else
                    {
                        _dataStore.MoveImage(image.Id, newPath);
                    }

                    var moved1 = moved;
                    Dispatcher.Invoke(() =>
                    {
                        image.Path = newPath;
                        _model.CurrentPositionScan = moved1;
                        _model.Status = $"Moving {_model.CurrentPositionScan:#,###,###} of {_model.TotalFilesScan:#,###,###}...";
                    });
                    moved++;
                }
                else
                {
                    _model.TotalFilesScan--;
                }
            }


            await Dispatcher.Invoke(async () =>
            {
                _model.Status = $"Moving {_model.TotalFilesScan:#,###,###} of {_model.TotalFilesScan:#,###,###}...";
                _model.TotalFilesScan = Int32.MaxValue;
                _model.CurrentPositionScan = 0;
                Toast($"{moved} files were moved.", "Move images");
            });

            //await _search.ReloadMatches();

            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = true;
            }


        }


        private (int, float) ScanFiles(IList<string> filesToScan, bool updateImages, CancellationToken cancellationToken)
        {
            var added = 0;
            var scanned = 0;

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var max = filesToScan.Count;

            Dispatcher.Invoke(() =>
            {
                _model.TotalFilesScan = max;
                _model.CurrentPositionScan = 0;
            });

            var folderIdCache = new Dictionary<string, int>();

            var newImages = new List<Image>();

            var includeProperties = new List<string>();

            if (_settings.AutoTagNSFW)
            {
                includeProperties.Add(nameof(Image.NSFW));
            }

            foreach (var file in Scanner.Scan(filesToScan))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                scanned++;

                if (file != null)
                {
                    var image = new Image()
                    {
                        Prompt = file.Prompt,
                        NegativePrompt = file.NegativePrompt,
                        Path = file.Path,
                        Width = file.Width,
                        Height = file.Height,
                        ModelHash = file.ModelHash,
                        Model = file.Model,
                        Steps = file.Steps,
                        Sampler = file.Sampler,
                        CFGScale = file.CFGScale,
                        Seed = file.Seed,
                        BatchPos = file.BatchPos,
                        BatchSize = file.BatchSize,
                        CreatedDate = File.GetCreationTime(file.Path),
                        AestheticScore = file.AestheticScore,
                        HyperNetwork = file.HyperNetwork,
                        HyperNetworkStrength = file.HyperNetworkStrength,
                        ClipSkip = file.ClipSkip,
                        FileSize = file.FileSize,
                        NoMetadata = file.NoMetadata
                    };

                    if (!string.IsNullOrEmpty(file.HyperNetwork) && !file.HyperNetworkStrength.HasValue)
                    {
                        file.HyperNetworkStrength = 1;
                    }

                    if (_settings.AutoTagNSFW)
                    {
                        if (_settings.NSFWTags.Any(t => image.Prompt != null && image.Prompt.ToLower().Contains(t.Trim().ToLower())))
                        {
                            image.NSFW = true;
                        }
                    }

                    newImages.Add(image);
                }

                if (newImages.Count == 100)
                {
                    if (updateImages)
                    {

                        added += _dataStore.UpdateImagesByPath(newImages, includeProperties, folderIdCache, cancellationToken);
                    }
                    else
                    {
                        _dataStore.AddImages(newImages, includeProperties, folderIdCache, cancellationToken);
                        added += newImages.Count;
                    }

                    newImages.Clear();
                }

                if (scanned % 33 == 0)
                {
                    Dispatcher.Invoke(() =>
                    {
                        _model.CurrentPositionScan = scanned;
                        _model.Status = $"Scanning {_model.CurrentPositionScan:#,###,##0} of {_model.TotalFilesScan:#,###,##0}...";
                    });
                }
            }

            if (newImages.Count > 0)
            {
                if (updateImages)
                {
                    added += _dataStore.UpdateImagesByPath(newImages, includeProperties, folderIdCache, cancellationToken);
                }
                else
                {
                    _dataStore.AddImages(newImages, includeProperties, folderIdCache, cancellationToken);
                    added += newImages.Count;
                }
            }

            Dispatcher.Invoke(() =>
            {
                _model.Status = $"Scanning {_model.TotalFilesScan:#,###,##0} of {_model.TotalFilesScan:#,###,##0}...";
                _model.TotalFilesScan = Int32.MaxValue;
                _model.CurrentPositionScan = 0;
            });

            stopwatch.Stop();

            var elapsedTime = stopwatch.ElapsedMilliseconds / 1000f;


            return (added, elapsedTime);
        }


        private async Task<bool> ScanInternal(IScanOptions settings, bool updateImages, bool reportIfNone, CancellationToken cancellationToken)
        {
            if (_model.IsScanning) return false;

            _model.IsScanning = true;

            var removed = 0;
            var added = 0;

            try
            {
                var existingImages = _dataStore.GetImagePaths().ToList();

                var removedList = existingImages.Where(img => !File.Exists(img.Path)).ToList();

                if (removedList.Any())
                {
                    removed = removedList.Count;
                    _dataStore.DeleteImages(removedList.Select(i => i.Id));
                }

                var filesToScan = new List<string>();

                foreach (var path in settings.ImagePaths)
                {
                    if (_scanCancellationTokenSource.IsCancellationRequested)
                    {
                        break;
                    }

                    if (Directory.Exists(path))
                    {
                        var ignoreFiles = updateImages ? null : existingImages.Where(p => p.Path.StartsWith(path)).Select(p => p.Path).ToHashSet();

                        filesToScan.AddRange(Scanner.GetFiles(path, settings.FileExtensions, ignoreFiles, settings.RecurseFolders.GetValueOrDefault(true), settings.ExcludePaths).ToList());
                    }
                }

                var (_added, elapsedTime) = ScanFiles(filesToScan, updateImages, cancellationToken);

                added = _added;

                if ((added + removed == 0 && reportIfNone) || added + removed > 0)
                {
                    Report(added, removed, elapsedTime, updateImages);
                }
            }
            catch (Exception ex)
            {
                await _messagePopupManager.ShowMedium(
                    ex.Message,
                    "Scan Error", PopupButtons.OK);
            }
            finally
            {
                _model.IsScanning = false;

            }


            return added + removed > 0;
        }

        private void Report(int added, int removed, float elapsedTime, bool updateImages)
        {
            Dispatcher.Invoke(() =>
            {
                if (added == 0 && removed == 0)
                {
                    Toast($"No new images found", "Scan Complete");
                }
                else
                {
                    var newOrOpdated = updateImages ? $"{added:#,###,##0} images updated" : $"{added:#,###,##0} new images added";

                    var missing = removed > 0 ? $"{removed:#,###,##0} missing images removed" : string.Empty;

                    var messages = new[] { newOrOpdated, missing };

                    var message = string.Join("\n", messages.Where(m => !string.IsNullOrEmpty(m)));

                    message = $"{message}";

                    if (updateImages)
                    {
                        Toast(message, "Rebuild Complete");
                    }
                    else
                    {
                        Toast(message, "Scan Complete", 10);
                    }
                }

                SetTotalFilesStatus();
            });

        }

        private void SetTotalFilesStatus()
        {
            var total = _dataStore.GetTotal();
            _model.Status = $"{total:###,###,##0} images in database";
        }

    }
}