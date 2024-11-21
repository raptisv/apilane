using System;
using System.Collections.Specialized;

namespace Apilane.Net.Request
{
    public class FilePostRequest : ApilaneRequestBase<FilePostRequest>
    {
        public static FilePostRequest New() => new();

        private string? _fileUid;
        private bool _public;
        private string? _fileName;

        private FilePostRequest() : base(null, "Files", "Post")
        {

        }

        public string GetFileName()
        {
            return _fileName is null || string.IsNullOrWhiteSpace(_fileName)
                ? Guid.NewGuid().ToString("N")
                : _fileName;
        }

        public FilePostRequest WithFileName(string fileName)
        {
            _fileName = fileName;
            return this;
        }

        public FilePostRequest WithPublicFlag(bool isPublic)
        {
            _public = isPublic;
            return this;
        }

        public FilePostRequest WithFileUID(string fileUid)
        {
            _fileUid = fileUid;
            return this;
        }

        protected override NameValueCollection GetExtraParams()
        {
            var extraParams = new NameValueCollection
            {
                { "public", _public.ToString().ToLower() }
            };

            if (!string.IsNullOrWhiteSpace(_fileUid))
            {
                extraParams.Add("uid", _fileUid);
            }

            return extraParams;
        }
    }
}
