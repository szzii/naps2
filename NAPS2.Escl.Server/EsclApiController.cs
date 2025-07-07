using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Xml.Linq;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using Microsoft.Extensions.Logging;
using NAPS2.Scan;
using NAPS2.Remoting.Server;
using System.Text;        // ← 添加这一行
using System.Web;  // 需要引用 System.Web.dll
using System.Text.Json;

namespace NAPS2.Escl.Server;

internal class EsclApiController : WebApiController
{
    private static readonly XNamespace ScanNs = EsclXmlHelper.ScanNs;
    private static readonly XNamespace PwgNs = EsclXmlHelper.PwgNs;

    private EsclDeviceConfig _deviceConfig;
    private readonly EsclServerState _serverState;
    private readonly EsclSecurityPolicy _securityPolicy;
    private readonly ILogger _logger;
    private readonly ScanController _scanController;
    private readonly ScanningContext _scanningContext;
    private readonly ScanServer _scanServer;
		private ScanDevice device;
    internal EsclApiController(
        EsclSecurityPolicy securityPolicy,EsclServerState serverState, ILogger logger, ScanController ScanController,ScanningContext ScanningContext,
				ScanServer scanServer)
    {
				_serverState = serverState;
        _securityPolicy = securityPolicy;
        _logger = logger;
				_scanController = ScanController;
				_scanningContext = ScanningContext;
				_scanServer = scanServer;
    }

    protected override void OnBeforeHandler()
    {
        base.OnBeforeHandler();
        if (_securityPolicy.HasFlag(EsclSecurityPolicy.ServerAllowAnyOrigin))
        {
						Response.Headers.Add("Access-Control-Allow-Origin", "*");
						// 允许的HTTP方法
            Response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS, HEAD, PUT, DELETE");
						// 允许的请求头
            Response.Headers.Add("Access-Control-Allow-Headers", "*");
        }
    }

		    // 添加OPTIONS方法处理
    [Route(HttpVerbs.Options, "/ScannerCapabilities/{base64Id}")]
    [Route(HttpVerbs.Options, "/ScannerStatus")]
    [Route(HttpVerbs.Options, "/ScanJobs")]
    [Route(HttpVerbs.Options, "/ScanJobs/{jobId}")]
    [Route(HttpVerbs.Options, "/NextDocument")]
    [Route(HttpVerbs.Options, "/Document")]
    [Route(HttpVerbs.Options, "/Ping")]
    [Route(HttpVerbs.Options, "/Devices")]
    public Task HandleOptions()
    {
        // 空响应，状态码200
        return Task.CompletedTask;
    }

		// 辅助：根据 cfg.Name 计算 Base64 ID
    private static string ComputeNameBase64(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name ?? "");
        return Convert.ToBase64String(bytes);
    }

    // 找设备时，用同样算法去生成 Base64，再对比
    private static ScanDevice? FindDeviceByBase64Id(IEnumerable<ScanDevice> devices, string base64Id)
    {

        if (string.IsNullOrWhiteSpace(base64Id))
            return null;

        // 如果你在生成时去掉了“=”填充，这里也要补齐
        int mod4 = base64Id.Length % 4;
        if (mod4 > 0) base64Id = base64Id.PadRight(base64Id.Length + (4 - mod4), '=');

        return devices.FirstOrDefault(d =>
            string.Equals(
                ComputeNameBase64(d.Name).TrimEnd('='),
                base64Id.TrimEnd('='), 
                StringComparison.OrdinalIgnoreCase));
    }

    [Route(HttpVerbs.Get, "/Devices")]
    public async Task Devices(){
				var devices = await _scanController.GetDeviceList(Driver.Wia);
				var list = devices.Select(cfg => new
				{
						Name = cfg.Name,
						Id   = ComputeNameBase64(cfg.Name).TrimEnd('=')
				}).ToArray();

				Response.ContentType = "application/json";
				var bytes = JsonSerializer.SerializeToUtf8Bytes(list);
				await Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
		}

    [Route(HttpVerbs.Get, "/ScannerCapabilities/{base64Id}")]
    public async Task GetScannerCapabilities(string base64Id)
    {

				var devices = await _scanController.GetDeviceList(Driver.Wia);
				if (devices == null || !devices.Any()){
						Response.StatusCode = 404;
						return;
				}

				var device = FindDeviceByBase64Id(devices,base64Id);
				if (device == null)
				{
						_logger.LogWarning("未找到 ID 为 {ID} 的扫描仪", base64Id);
						Response.StatusCode = 404;
						return;
				}

				_deviceConfig = _scanServer.MakeEsclDeviceConfig2(device);

        var caps = _deviceConfig.Capabilities;
        var protocol = _securityPolicy.HasFlag(EsclSecurityPolicy.ServerRequireHttps) ? "https" : "http";
        var iconUri = caps.IconPng != null ? $"{protocol}://naps2-{caps.Uuid}.local.:{_deviceConfig.Port}/eSCL/icon.png" : "";
        var doc =
            EsclXmlHelper.CreateDocAsString(
                new XElement(ScanNs + "ScannerCapabilities",
                    new XElement(PwgNs + "Version", caps.Version),
                    new XElement(PwgNs + "MakeAndModel", caps.MakeAndModel),
                    new XElement(PwgNs + "SerialNumber", caps.SerialNumber),
                    new XElement(ScanNs + "Manufacturer", caps.Manufacturer),
                    new XElement(ScanNs + "UUID", caps.Uuid),
                    new XElement(ScanNs + "AdminURI", ""),
                    new XElement(ScanNs + "IconURI", iconUri),
                    new XElement(ScanNs + "Naps2Extensions", "Progress;ErrorDetails;ShortTimeout;AnyDpi"),
                    new XElement(ScanNs + "Platen",
                        new XElement(ScanNs + "PlatenInputCaps", GetCommonInputCaps())),
                    new XElement(ScanNs + "Adf",
                        new XElement(ScanNs + "AdfSimplexInputCaps", GetCommonInputCaps()),
                        new XElement(ScanNs + "AdfDuplexInputCaps", GetCommonInputCaps())),
                    new XElement(ScanNs + "CompressionFactorSupport",
                        new XElement(ScanNs + "Min", 0),
                        new XElement(ScanNs + "Max", 100),
                        new XElement(ScanNs + "Normal", 75),
                        new XElement(ScanNs + "Step", 1))));
        Response.ContentType = "text/xml";
        using var writer = new StreamWriter(HttpContext.OpenResponseStream());
        await writer.WriteAsync(doc);
    }

    private object[] GetCommonInputCaps()
    {
        // TODO: After implementing scanner capabilities this should be scanner-specific
        return
        [
            new XElement(ScanNs + "MinWidth", "1"),
            new XElement(ScanNs + "MaxWidth", EsclInputCaps.DEFAULT_MAX_WIDTH),
            new XElement(ScanNs + "MinHeight", "1"),
            new XElement(ScanNs + "MaxHeight", EsclInputCaps.DEFAULT_MAX_HEIGHT),
            new XElement(ScanNs + "MaxScanRegions", "1"),
            new XElement(ScanNs + "SettingProfiles",
                new XElement(ScanNs + "SettingProfile",
                    new XElement(ScanNs + "ColorModes",
                        new XElement(ScanNs + "ColorMode", "BlackAndWhite1"),
                        new XElement(ScanNs + "ColorMode", "Grayscale8"),
                        new XElement(ScanNs + "ColorMode", "RGB24")),
                    new XElement(ScanNs + "DocumentFormats",
                        new XElement(PwgNs + "DocumentFormat", "application/pdf"),
                        new XElement(PwgNs + "DocumentFormat", "image/jpeg"),
                        new XElement(PwgNs + "DocumentFormat", "image/png"),
                        new XElement(ScanNs + "DocumentFormatExt", "application/pdf"),
                        new XElement(ScanNs + "DocumentFormatExt", "image/jpeg"),
                        new XElement(ScanNs + "DocumentFormatExt", "image/png")
                    ),
                    new XElement(ScanNs + "SupportedResolutions",
                        new XElement(ScanNs + "DiscreteResolutions",
                            CreateResolution(100),
                            CreateResolution(150),
                            CreateResolution(200),
                            CreateResolution(300),
                            CreateResolution(400),
                            CreateResolution(600),
                            CreateResolution(800),
                            CreateResolution(1200),
                            CreateResolution(2400),
                            CreateResolution(4800)
                        ))))
        ];
    }

    private XElement CreateResolution(int res) =>
        new(ScanNs + "DiscreteResolution",
            new XElement(ScanNs + "XResolution", res.ToString()),
            new XElement(ScanNs + "YResolution", res.ToString()));

    [Route(HttpVerbs.Get, "/icon.png")]
    public async Task GetIcon()
    {
        if (_deviceConfig.Capabilities.IconPng != null)
        {
            Response.ContentType = "image/png";
            using var stream = Response.OutputStream;
            var buffer = _deviceConfig.Capabilities.IconPng;
            await stream.WriteAsync(buffer, 0, buffer.Length);
        }
        else
        {
            Response.StatusCode = 404;
        }
    }

    [Route(HttpVerbs.Get, "/ScannerStatus")]
    public async Task GetScannerStatus()
    {
        var jobsElement = new XElement(ScanNs + "Jobs");
        foreach (var jobInfo in _serverState.Jobs.OrderBy(x => x.LastUpdated.ElapsedMilliseconds))
        {
            jobsElement.Add(new XElement(ScanNs + "JobInfo",
                new XElement(PwgNs + "JobUri", $"/eSCL/ScanJobs/{jobInfo.Id}"),
                new XElement(PwgNs + "JobUuid", jobInfo.Id),
                new XElement(ScanNs + "Age", Math.Ceiling(jobInfo.LastUpdated.Elapsed.TotalSeconds)),
                new XElement(PwgNs + "ImagesCompleted", jobInfo.ImagesCompleted),
                new XElement(PwgNs + "ImagesToTransfer", jobInfo.ImagesToTransfer),
                new XElement(PwgNs + "JobState", jobInfo.State.ToString()),
                new XElement(PwgNs + "JobStateReasons",
                    new XElement(PwgNs + "JobStateReason",
                        jobInfo.State == EsclJobState.Processing ? "JobScanning" : "JobCompletedSuccessfully"))));
        }
        var scannerState = _serverState.IsProcessing ? EsclScannerState.Processing : EsclScannerState.Idle;
        var adfState = _serverState.IsProcessing ? EsclAdfState.ScannerAdfProcessing : EsclAdfState.ScannedAdfLoaded;
        var doc =
            EsclXmlHelper.CreateDocAsString(
                new XElement(ScanNs + "ScannerStatus",
                    new XElement(PwgNs + "Version", "2.6"),
                    new XElement(PwgNs + "State", scannerState),
                    new XElement(ScanNs + "AdfState", adfState),
                    jobsElement
                ));
        Response.ContentType = "text/xml";
        using var writer = new StreamWriter(HttpContext.OpenResponseStream());
        await writer.WriteAsync(doc);
    }

		[Route(HttpVerbs.Post, "/ScanJobs")]
		public async Task CreateScanJob()
		{
				var base64Id = HttpContext.Request.QueryString["Id"];
				var devices = await _scanController.GetDeviceList(Driver.Wia);
				if (devices == null || !devices.Any()){
						Response.StatusCode = 404;
						return;
				}

				var device = FindDeviceByBase64Id(devices,base64Id);
				if (device == null)
				{
						_logger.LogWarning("未找到 ID 为 {ID} 的扫描仪", base64Id);
						Response.StatusCode = 404;
						return;
				}

				// 1. 解析设置
				EsclScanSettings settings;
				try
				{
						var doc = XDocument.Load(Request.InputStream);
						settings = SettingsParser.Parse(doc);
				}
				catch (Exception)
				{
						Response.StatusCode = 400;
						return;
				}

				// 2. 如果已经在处理，返回 503
				if (_serverState.IsProcessing)
				{
						Response.StatusCode = 503;
						return;
				}

				try
				{
						//job = new ScanJob(_scanningContext, new ScanController(_scanningContext), device, settings);
						// 4. 成功，注册作业并返回 201 + Location 头

						_serverState.IsProcessing = true;
						var jobInfo = JobInfo.CreateNewJob(_serverState,new ScanJob(_scanningContext, new ScanController(_scanningContext), device, settings));
						_serverState.AddJob(jobInfo);
						// 构建 Location URI
						var uri = Request.Url;
						if (Request.IsSecureConnection)
						{
								// EmbedIO HTTPS 修正
								uri = new UriBuilder(uri) { Scheme = "https" }.Uri;
						}

						Response.Headers.Add("Access-Control-Expose-Headers", "Location");
						Response.Headers.Add("Location", $"{uri}/{jobInfo.Id}");
						Response.StatusCode = 201;
				}
				catch (Exception ex)
				{
						_logger.LogError(ex, "创建 ScanJob 时出错");
						// 先设置状态码，再写入响应体，且不要用 using() 去自动关闭底层流
						Response.StatusCode = 501;
						Response.ContentType = "text/plain; charset=utf-8";
						using var writer = new StreamWriter(HttpContext.OpenResponseStream());
						await writer.WriteAsync(ex.ToString());
						return;
				}
		}


    [Route(HttpVerbs.Delete, "/ScanJobs/{jobId}")]
    public void CancelScanJob(string jobId)
    {
        if (_serverState.TryGetJob(jobId, out var jobState) &&
            jobState.State is EsclJobState.Pending or EsclJobState.Processing)
        {
            jobState.Job.Cancel();
        }
        else
        {
            Response.StatusCode = 404;
        }
    }

    [Route(HttpVerbs.Get, "/ScanJobs/{jobId}/ScanImageInfo")]
    public void GetImageinfo(string jobId)
    {
    }

    // This endpoint is a NAPS2-specific extension to the ESCL API.
    // It gives a chunked response where each line is a double between 0 and 1 representing the current page progress.
    // This endpoint should be called once before each call to NextDocument.
    [Route(HttpVerbs.Get, "/ScanJobs/{jobId}/Progress")]
    public async Task Progress(string jobId)
    {
        if (_serverState.TryGetJob(jobId, out var jobState) &&
            jobState.State is EsclJobState.Pending or EsclJobState.Processing)
        {
            SetChunkedResponse();
            using var stream = Response.OutputStream;
            await jobState.Job.WriteProgressTo(stream);
        }
        else
        {
            Response.StatusCode = 404;
        }
    }

    // This endpoint is a NAPS2-specific extension to the ESCL API.
    // The ESCL status-based model (where errors like "no paper in feeder" are encompassed by ScannerStatus that can be
    // polled every few seconds) is a good match to a physical scanner but a poor match to NAPS2's model.
    // Instead, we have a this ErrorDetails endpoint that we call when NextDocument returns a 500 error which gives us
    // XML-based details for the exception that occurred.
    [Route(HttpVerbs.Get, "/ScanJobs/{jobId}/ErrorDetails")]
    public async Task ErrorDetails(string jobId)
    {
        if (_serverState.TryGetJob(jobId, out var jobState))
        {
            Response.ContentType = "text/xml";
            using var stream = Response.OutputStream;
            await jobState.Job.WriteErrorDetailsTo(stream);
        }
        else
        {
            Response.StatusCode = 404;
        }
    }

    [Route(HttpVerbs.Get, "/ScanJobs/{jobId}/NextDocument")]
    public async Task NextDocument(string jobId)
    {
        if (!CheckJobState(jobId, out var jobInfo))
        {
            return;
        }

        await jobInfo.NextDocumentLock.Take();
        try
        {
            // Recheck job state in case it's been changed while we were waiting on the lock
            if (!CheckJobState(jobId, out _))
            {
                return;
            }
            await WaitForAndWriteNextDocument(jobInfo);
        }
        finally
        {
            jobInfo.NextDocumentLock.Release();
        }
    }

    private bool CheckJobState(string jobId, [NotNullWhen(true)] out JobInfo? jobInfo)
    {
        if (!_serverState.TryGetJob(jobId, out jobInfo))
        {
            Response.StatusCode = 404;
            return false;
        }
        if (jobInfo.State == EsclJobState.Aborted)
        {
            Response.StatusCode = 500;
            return false;
        }
        if (jobInfo.State is not (EsclJobState.Pending or EsclJobState.Processing))
        {
            Response.StatusCode = 404;
            return false;
        }
        return true;
    }

    private async Task WaitForAndWriteNextDocument(JobInfo jobInfo)
    {
        try
        {
            // If we already have a document (i.e. if a connection error occured during the previous NextDocument
            // request), we stay at that same document and don't advance
            var cts = new CancellationTokenSource();
            cts.CancelAfter(1000);
            jobInfo.NextDocumentReady = jobInfo.NextDocumentReady || await jobInfo.Job.WaitForNextDocument(cts.Token);
        }
        catch (TaskCanceledException)
        {
            _logger.LogDebug("Waiting for document timed out, returning 503");
            // Tell the client to retry after 2s
            Response.Headers.Add("Retry-After", "2");
            Response.StatusCode = 503;
            return;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ESCL server error waiting for document");
            jobInfo.TransitionState(EsclJobState.Processing, EsclJobState.Aborted);
            Response.StatusCode = 500;
            return;
        }

        // At this point either we have a document and can respond with it, or we have no documents left and should 404
        if (jobInfo.NextDocumentReady)
        {
            try
            {
                Response.Headers.Add("Content-Location", $"/eSCL/ScanJobs/{jobInfo.Id}/1");
                SetChunkedResponse();
                Response.ContentType = jobInfo.Job.ContentType;
                Response.ContentEncoding = null;
                using var stream = Response.OutputStream;
                await jobInfo.Job.WriteDocumentTo(stream);
                jobInfo.NextDocumentReady = false;
                jobInfo.TransferredDocument();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ESCL server error writing document");
                // We don't transition state here, as the assumption is that the problem was network-related and the
                // client will retry
                Response.StatusCode = 500;
            }
        }
        else
        {
            jobInfo.TransitionState(EsclJobState.Processing, EsclJobState.Completed);
            Response.StatusCode = 404;
        }
    }

    private void SetChunkedResponse()
    {
        // Bypass https://github.com/unosquare/embedio/issues/510
        var field = Response.GetType().GetField("<ProtocolVersion>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field != null)
        {
            field.SetValue(Response, new Version(1, 1));
        }
        Response.SendChunked = true;
    }
}
