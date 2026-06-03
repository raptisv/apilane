using Apilane.Common.Utilities;

namespace Apilane.UnitTests
{
    [TestClass]
    public class MimeTypeDetectorTests
    {
        [TestMethod]
        public void GetMimeType_KnownImageExtensions_Should_Return_ImageMimeTypes()
        {
            Assert.AreEqual("image/jpeg", MimeTypeDetector.GetMimeType("photo.jpg"));
            Assert.AreEqual("image/jpeg", MimeTypeDetector.GetMimeType("photo.jpeg"));
            Assert.AreEqual("image/png", MimeTypeDetector.GetMimeType("image.png"));
            Assert.AreEqual("image/gif", MimeTypeDetector.GetMimeType("image.gif"));
            Assert.AreEqual("image/bmp", MimeTypeDetector.GetMimeType("image.bmp"));
            Assert.AreEqual("image/svg+xml", MimeTypeDetector.GetMimeType("image.svg"));
            Assert.AreEqual("image/webp", MimeTypeDetector.GetMimeType("image.webp"));
        }

        [TestMethod]
        public void GetMimeType_KnownDocumentExtensions_Should_Return_DocumentMimeTypes()
        {
            Assert.AreEqual("application/pdf", MimeTypeDetector.GetMimeType("file.pdf"));
            Assert.AreEqual("application/msword", MimeTypeDetector.GetMimeType("file.doc"));
            Assert.AreEqual("application/vnd.openxmlformats-officedocument.wordprocessingml.document", MimeTypeDetector.GetMimeType("file.docx"));
            Assert.AreEqual("application/vnd.ms-excel", MimeTypeDetector.GetMimeType("file.xls"));
            Assert.AreEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", MimeTypeDetector.GetMimeType("file.xlsx"));
            Assert.AreEqual("application/vnd.ms-powerpoint", MimeTypeDetector.GetMimeType("file.ppt"));
            Assert.AreEqual("application/vnd.openxmlformats-officedocument.presentationml.presentation", MimeTypeDetector.GetMimeType("file.pptx"));
        }

        [TestMethod]
        public void GetMimeType_KnownTextExtensions_Should_Return_TextMimeTypes()
        {
            Assert.AreEqual("text/plain", MimeTypeDetector.GetMimeType("file.txt"));
            Assert.AreEqual("text/csv", MimeTypeDetector.GetMimeType("file.csv"));
            Assert.AreEqual("application/json", MimeTypeDetector.GetMimeType("file.json"));
            Assert.AreEqual("application/xml", MimeTypeDetector.GetMimeType("file.xml"));
            Assert.AreEqual("text/html", MimeTypeDetector.GetMimeType("file.html"));
            Assert.AreEqual("text/css", MimeTypeDetector.GetMimeType("file.css"));
            Assert.AreEqual("application/javascript", MimeTypeDetector.GetMimeType("file.js"));
        }

        [TestMethod]
        public void GetMimeType_KnownArchiveAudioVideoExtensions_Should_Return_MimeTypes()
        {
            Assert.AreEqual("application/zip", MimeTypeDetector.GetMimeType("file.zip"));
            Assert.AreEqual("application/vnd.rar", MimeTypeDetector.GetMimeType("file.rar"));
            Assert.AreEqual("application/x-7z-compressed", MimeTypeDetector.GetMimeType("file.7z"));
            Assert.AreEqual("application/x-tar", MimeTypeDetector.GetMimeType("file.tar"));
            Assert.AreEqual("application/gzip", MimeTypeDetector.GetMimeType("file.gz"));
            Assert.AreEqual("audio/mpeg", MimeTypeDetector.GetMimeType("file.mp3"));
            Assert.AreEqual("audio/wav", MimeTypeDetector.GetMimeType("file.wav"));
            Assert.AreEqual("video/mp4", MimeTypeDetector.GetMimeType("file.mp4"));
            Assert.AreEqual("video/x-msvideo", MimeTypeDetector.GetMimeType("file.avi"));
            Assert.AreEqual("video/quicktime", MimeTypeDetector.GetMimeType("file.mov"));
            Assert.AreEqual("video/x-matroska", MimeTypeDetector.GetMimeType("file.mkv"));
        }

        [TestMethod]
        public void GetMimeType_Should_Be_Case_Insensitive()
        {
            Assert.AreEqual("image/jpeg", MimeTypeDetector.GetMimeType("PHOTO.JPG"));
            Assert.AreEqual("application/pdf", MimeTypeDetector.GetMimeType("REPORT.PDF"));
            Assert.AreEqual("application/json", MimeTypeDetector.GetMimeType("DATA.Json"));
        }

        [TestMethod]
        public void GetMimeType_UnknownOrMissingExtension_Should_Return_Default()
        {
            Assert.AreEqual("application/octet-stream", MimeTypeDetector.GetMimeType("file.unknown"));
            Assert.AreEqual("application/octet-stream", MimeTypeDetector.GetMimeType("file"));
            Assert.AreEqual("application/octet-stream", MimeTypeDetector.GetMimeType(string.Empty));
        }
    }
}
