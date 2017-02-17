using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Splat;

namespace Squirrel
{
    public interface IFileDownloader
    {
        Task DownloadFile(string url, string targetFile, Action<int> progress);
        Task<byte[]> DownloadUrl(string url);
    }

    public class FileDownloader : IFileDownloader, IEnableLogger
    {
        private readonly WebClient _providedClient;

        public FileDownloader(WebClient providedClient = null)
        {
            _providedClient = providedClient;
        }

        public async Task DownloadFile(string url, string targetFile, Action<int> progress)
        {
            using (var wc = _providedClient ?? Utility.CreateWebClient())
            {
                var failedUrl = default(string);
                progress = progress ?? (s => { });

                var lastSignalled = DateTime.MinValue;
                wc.DownloadProgressChanged += (sender, args) =>
                {
                    var now = DateTime.Now;

                    if (now - lastSignalled > TimeSpan.FromMilliseconds(500))
                    {
                        lastSignalled = now;
                        progress(args.ProgressPercentage);
                    }
                };

                retry:
                try
                {
                    this.Log().Info("Downloading file: " + (failedUrl ?? url));

                    await this.WarnIfThrows(() => wc.DownloadFileTaskAsync(failedUrl ?? url, targetFile),
                        "Failed downloading URL: " + (failedUrl ?? url));
                }
                catch (Exception)
                {
                    // NB: Some super brain-dead services are case-sensitive yet 
                    // corrupt case on upload. I can't even.
                    if (failedUrl != null) throw;

                    failedUrl = url.ToLower();
                    progress(0);
                    goto retry;
                }
            }
        }

        public async Task<byte[]> DownloadUrl(string url)
        {
            using (var wc = _providedClient ?? Utility.CreateWebClient())
            {
                var failedUrl = default(string);

                retry:
                try
                {
                    this.Log().Info("Downloading url: " + (failedUrl ?? url));

                    return await this.WarnIfThrows(() => wc.DownloadDataTaskAsync(failedUrl ?? url),
                        "Failed to download url: " + (failedUrl ?? url));
                }
                catch (Exception)
                {
                    // NB: Some super brain-dead services are case-sensitive yet 
                    // corrupt case on upload. I can't even.
                    if (failedUrl != null) throw;

                    failedUrl = url.ToLower();
                    goto retry;
                }
            }
        }
    }

    public class FtpFileDownloader : IFileDownloader, IEnableLogger
    {
        public Task DownloadFile(string url, string targetFile, Action<int> progress)
        {
            if (url.Contains("?"))
            {
                url = url.Substring(0, url.IndexOf("?"));
            }

            this.Log().Info("Downloading  ftp file: " + (url));

            progress = progress ?? (s => { });

            var request = (FtpWebRequest)WebRequest.Create(url);
            request.Method = WebRequestMethods.Ftp.DownloadFile;

            request.Credentials = new NetworkCredential("anonymous", "janeDoe@contoso.com");

            using (var response = (FtpWebResponse)request.GetResponse())
            {
                using (var file = new FileStream(targetFile, FileMode.Create))
                {
                    using (var reader = response.GetResponseStream())
                    {
                        while (true)
                        {
                            byte[] buff = new byte[2048];
                            var read = reader.Read(buff, 0, buff.Length);
                            if (read == 0)
                            {
                                break;
                            }
                            file.Write(buff, 0, read);
                        }
                    }
                    file.Close();
                }
            }

            this.Log().Info("Downloading file ftp finihed: " + (url));

            return Task.Delay(0);
        }

        public Task<byte[]> DownloadUrl(string url)
        {
            if (url.Contains("?"))
            {
                url = url.Substring(0, url.IndexOf("?"));
            }
            this.Log().Info("Downloading ftp url: " + (url));

            var request = (FtpWebRequest)WebRequest.Create(url);
            request.Method = WebRequestMethods.Ftp.DownloadFile;

            request.Credentials = new NetworkCredential("anonymous", "janeDoe@contoso.com");

            var response = (FtpWebResponse)request.GetResponse();

            var content = new List<byte>();
            using (var reader = new StreamReader(response.GetResponseStream()))
            {
                while (true)
                {
                    var buff = new char[2048];
                    var read = reader.Read(buff, 0, buff.Length);
                    if (read == 0)
                    {
                        break;
                    }
                    content.AddRange(Encoding.UTF8.GetBytes(buff.Take(read).ToArray()));
                }
            }

            response.Close();
            this.Log().Info("Finished downloadign ftp url: " + (url));


            return Task.FromResult(content.ToArray());
        }
    }
}
