using System.Collections.Specialized;

namespace Apilane.Net.Request
{
    public class FileDownloadRequest : ApilaneRequestBase<FileDownloadRequest>
    {
        public static FileDownloadRequest New() => new();

        private string? _fileUid;
        private long? _fileId;

        private FileDownloadRequest() : base(null, "Files", "Download")
        {

        }

        public FileDownloadRequest WithFileID(long fileId)
        {
            _fileId = fileId;
            return this;
        }

        public FileDownloadRequest WithFileUID(string fileUid)
        {
            _fileUid = fileUid;
            return this;
        }

        protected override NameValueCollection GetExtraParams()
        {
            var extraParams = new NameValueCollection();

            if (_fileId.HasValue)
            {
                extraParams.Add("fileID", _fileId.Value.ToString());
            }

            if (!string.IsNullOrWhiteSpace(_fileUid))
            {
                extraParams.Add("fileUID", _fileUid);
            }

            return extraParams;
        }
    }
}
