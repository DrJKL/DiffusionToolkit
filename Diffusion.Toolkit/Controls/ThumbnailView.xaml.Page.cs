﻿namespace Diffusion.Toolkit.Controls
{
    public class PageChangedEventArgs
    {
        public int Page { get; set; }
        public bool GotoEnd { get; set; }
    }

    public partial class ThumbnailView
    {
        public void GoFirstPage()
        {
            Model.Page = 1;

            var args = new PageChangedEventArgs()
            {
                Page = Model.Page
            };

            PageChangedCommand?.Execute(args);
        }

        public void GoLastPage()
        {
            Model.Page = Model.Pages;
            
            var args = new PageChangedEventArgs()
            {
                Page = Model.Page
            };

            PageChangedCommand?.Execute(args);
        }

        public void GoPrevPage(bool gotoEnd = false)
        {
            if (Model.Page > 1)
            {
                Model.Page--;

                var args = new PageChangedEventArgs()
                {
                    Page = Model.Page,
                    GotoEnd = gotoEnd
                };

                PageChangedCommand?.Execute(args);
            }

        }

        public void GoNextPage()
        {
            if (Model.Page < Model.Pages)
            {
                Model.Page++;

                var args = new PageChangedEventArgs()
                {
                    Page = Model.Page
                };

                PageChangedCommand?.Execute(args);
            }
        }

        public void SetPagingEnabled()
        {
            Model.SetPagingEnabled(Model.Page);
        }
    }
}
