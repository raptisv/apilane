using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using WebOptimizer;

namespace Apilane.Portal.Extensions
{
    public static class AssetsDependencyInjection
    {
        public static IServiceCollection AddAssets(this IServiceCollection services)
        {
            services
                .AddWebOptimizer(pipeline =>
                {
                    Main(pipeline);
                    Data(pipeline);
                    Account(pipeline);
                    CodeMirror(pipeline);
                });

            return services;
        }

        private static void Account(IAssetPipeline pipeline)
        {
            // js
            var jsList = new List<string>()
            {
                "assets/vendor/bootstrap/bootstrap.bundle.min.js",
                "assets/vendor/jquery/jquery-3.7.1.min.js"
            };
            pipeline.MinifyJsFiles(jsList.ToArray());
            pipeline.AddJavaScriptBundle("account.min.js", jsList.ToArray());

            // css
            var cssList = new List<string>()
            {
                "assets/vendor/fastbootstrap/fastbootstrap.min.css",
                "assets/account.css"
            };
            pipeline.MinifyCssFiles(cssList.ToArray());
            pipeline.AddCssBundle("account.min.css", cssList.ToArray());
        }

        private static void CodeMirror(IAssetPipeline pipeline)
        {
            // js
            var jsList = new List<string>()
            {
                "assets/vendor/codemirror-5.40.2/codemirror.js",
                "assets/vendor/codemirror-5.40.2/mode/sql/sql.js",
                "assets/vendor/codemirror-5.40.2/edit/matchbrackets.js",
                "assets/vendor/codemirror-5.40.2/show-hint.js",
                "assets/vendor/codemirror-5.40.2/sql-hint.js"
            };
            pipeline.MinifyJsFiles(jsList.ToArray());
            pipeline.AddJavaScriptBundle("codemirror.min.js", jsList.ToArray());

            // css
            var cssList = new List<string>()
            {
                "assets/vendor/codemirror-5.40.2/codemirror.css",
                "assets/vendor/codemirror-5.40.2/material.css"
            };
            pipeline.MinifyCssFiles(cssList.ToArray());
            pipeline.AddCssBundle("codemirror.min.css", cssList.ToArray());
        }

        private static void Data(IAssetPipeline pipeline)
        {
            // css
            var cssList = new List<string>()
            {
                "assets/data.css",
                "assets/vendor/sweetalert/sweetalert.css",
                "assets/vendor/fastbootstrap/fastbootstrap.min.css",
                "assets/vendor/bootstrap-icons/font/bootstrap-icons.css"
            };
            pipeline.MinifyCssFiles(cssList.ToArray());
            pipeline.AddCssBundle("data.min.css", cssList.ToArray());

            // js
            var jsList = new List<string>()
            {
                "assets/vendor/sweetalert/sweetalert.min.js",
                "assets/vendor/jquery/jquery-3.7.1.min.js",
                "assets/vendor/bootstrap/bootstrap.bundle.min.js",
                "assets/vendor/moment/moment.js",
                "assets/custom.js",
                "assets/vendor/magnific-popup/jquery.magnific-popup.min.js",
                "assets/vendor/jquery-file-upload/jquery.ui.widget.js",
                "assets/vendor/jquery-file-upload/jquery.iframe-transport.js",
                "assets/vendor/jquery-file-upload/jquery.fileupload.js"
            };
            pipeline.MinifyJsFiles(jsList.ToArray());
            pipeline.AddJavaScriptBundle("data.min.js", jsList.ToArray());
        }

        private static void Main(IAssetPipeline pipeline)
        {
            // css
            var cssList = new List<string>()
            {
                "assets/vendor/fastbootstrap/fastbootstrap.min.css",
                "assets/vendor/bootstrap-icons/font/bootstrap-icons.css",
                "assets/vendor/magnific-popup/magnific-popup.css",
                "assets/custom.css"
            };
            pipeline.MinifyCssFiles(cssList.ToArray());
            pipeline.AddCssBundle("site.min.css", cssList.ToArray());

            // js
            var jsList = new List<string>()
            {
                "assets/vendor/bootstrap/bootstrap.bundle.min.js",
                "assets/vendor/jquery/jquery-3.7.1.min.js",
                "assets/vendor/magnific-popup/jquery.magnific-popup.min.js",
                "assets/vendor/sweetalert/sweetalert2.min.js",
                "assets/vendor/jquery-ui/jquery-ui.js",
                "assets/custom.js",
                "assets/vendor/chartjs/chart.min.js",
                "assets/vendor/chartjs/chartjs-plugin-colorschemes.min.js",
                "assets/vendor/bootstrap-notify/bootstrap-notify.min.js",
                "assets/vendor/jquery-file-upload/jquery.ui.widget.js",
                "assets/vendor/jquery-file-upload/jquery.iframe-transport.js",
                "assets/vendor/jquery-file-upload/jquery.fileupload.js"
            };
            pipeline.MinifyJsFiles(jsList.ToArray());
            pipeline.AddJavaScriptBundle("site.min.js", jsList.ToArray());
        }
    }
}