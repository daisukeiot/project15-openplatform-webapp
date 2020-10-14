﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Permissions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Devices;
using Portal.Models;
using System.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Devices.Common.Exceptions;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Runtime.ConstrainedExecution;
using Portal.Helper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Azure.Devices.Shared;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Azure.DigitalTwins.Parser;
using System.Net;

namespace Portal.Controllers
{
    public class IoTHubController : Controller
    {
//        private const string _repositoryEndpoint = "https://devicemodels.azure.com";
        private const string _repositoryEndpoint = "https://raw.githubusercontent.com/daisukeiot/azure-dtdl-repository/main";
        private readonly ILogger<IoTHubController> _logger;
        private readonly AppSettings _appSettings;
        private string _gitToken;
        private IIoTHubHelper _helper;

        public IoTHubController(IIoTHubHelper helper, IOptions<AppSettings> optionsAccessor)
        {
            _helper = helper;
            _appSettings = optionsAccessor.Value;
            _gitToken = _appSettings.GitHub.token;
        }

        private IoTHubController(IIoTHubHelper helper, IOptions<AppSettings> optionsAccessor, ILogger<IoTHubController> logger)
        {
            _logger = logger;
            _appSettings = optionsAccessor.Value;
        }

        public IActionResult Index()
        {
            return View();
        }

        private static bool IsValidDtmi(string dtmi)
        {
            // Regex defined at https://github.com/Azure/digital-twin-model-identifier#validation-regular-expressions
            Regex rx = new Regex(@"^dtmi:[A-Za-z](?:[A-Za-z0-9_]*[A-Za-z0-9])?(?::[A-Za-z](?:[A-Za-z0-9_]*[A-Za-z0-9])?)*;[1-9][0-9]{0,8}$");
            return rx.IsMatch(dtmi);
        }

        private static string DtmiToPath(string dtmi)
        {
            if (!IsValidDtmi(dtmi))
            {
                return null;
            }
            // dtmi:com:example:Thermostat;1 -> dtmi/com/example/thermostat-1.json
            return $"/{dtmi.ToLowerInvariant().Replace(":", "/").Replace(";", "-")}.json";
        }

        private async Task<string> Resolve(string dtmi)
        {
            if (string.IsNullOrEmpty(dtmi))
            {
                return string.Empty;
            }

            WebClient wc = new WebClient();
            // Apply model repository convention
            string dtmiPath = DtmiToPath(dtmi.ToString());
            if (string.IsNullOrEmpty(dtmiPath))
            {
                Console.WriteLine($"Invalid DTMI: {dtmi}");
                return await Task.FromResult<string>(string.Empty);
            }
            string fullyQualifiedPath = $"{_repositoryEndpoint}{dtmiPath}";

            // Make request
            // string modelContent = await _httpClient.GetStringAsync(fullyQualifiedPath);
            var token = $"token {_gitToken}";
            wc.Headers.Add("Authorization", token);
            var modelContent = wc.DownloadString(fullyQualifiedPath);

            return modelContent;
        }

        //static async Task<IEnumerable<string>> ResolveCallback(IReadOnlyCollection<Dtmi> dtmis)
        //{
        //    Console.WriteLine("ResolveCallback invoked!");
        //    List<string> result = new List<string>();

        //    foreach (Dtmi dtmi in dtmis)
        //    {
        //        string content = await Resolve(dtmi.ToString());
        //        result.Add(content);
        //    }

        //    return result;
        //}

        public async Task<IEnumerable<string>> DtmiResolver(IReadOnlyCollection<Dtmi> dtmis)
        {
            List<String> jsonLds = new List<string>();
            var wc = new WebClient();

            foreach (var dtmi in dtmis)
            {
                Console.WriteLine("Resolver looking for. " + dtmi);
                string model = dtmi.OriginalString.Replace(":", "/");
                model = (model.Replace(";", "-")).ToLower();
                if (!String.IsNullOrWhiteSpace(model))
                {
                    var dtmiContent = await Resolve(dtmi.OriginalString);
                    jsonLds.Add(dtmiContent);
                }
            }
            return jsonLds;
        }

        [HttpGet]
        public async Task<ActionResult> GetDevice(string deviceId)
        {
            Device device = null;
            Twin twin = null;
            DEVICE_DATA deviceData = new DEVICE_DATA();
            deviceData.telemetry = new List<TELEMETRY_DATA>();

            try
            {
                device = await _helper.GetDevice(deviceId).ConfigureAwait(false);
                twin = await _helper.GetTwin(deviceId).ConfigureAwait(false);

                if (device == null)
                {
                    return BadRequest();
                }

                var jsonData = JsonConvert.SerializeObject(device);

                deviceData.deviceId = device.Id;
                deviceData.connectionState = device.ConnectionState.ToString();
                deviceData.status = device.Status.ToString();
                deviceData.authenticationType = device.Authentication.Type.ToString();
            
                if (device.Authentication.Type == AuthenticationType.Sas)
                {
                    deviceData.primaryKey = device.Authentication.SymmetricKey.PrimaryKey;
                    deviceData.secondaryKey = device.Authentication.SymmetricKey.SecondaryKey;
                }

                if (twin != null)
                {
                    JObject twinJson = (JObject)JsonConvert.DeserializeObject(twin.ToJson());

                    if (twinJson.ContainsKey("modelId"))
                    {
                        deviceData.modelId = twin.ModelId;

                        var dtmiContent = await Resolve(deviceData.modelId);

                        if (!string.IsNullOrEmpty(dtmiContent))
                        {
                            ModelParser parser = new ModelParser();
                            parser.DtmiResolver = DtmiResolver;
                            //var parsedDtmis = await parser.ParseAsync(models.Values);
                            var parsedDtmis = await parser.ParseAsync(new List<string> { dtmiContent });
                            Console.WriteLine("Parsing success!");

                            var interfaces = parsedDtmis.Where(r => r.Value.EntityKind == DTEntityKind.Telemetry).ToList();
                            foreach (var dt in interfaces)
                            {
                                TELEMETRY_DATA data = new TELEMETRY_DATA();
                                DTTelemetryInfo telemetryInfo = dt.Value as DTTelemetryInfo;
                                if (telemetryInfo.DisplayName.Count > 0)
                                {
                                    data.TelemetryDisplayName = telemetryInfo.DisplayName["en"];
                                }

                                if (telemetryInfo.SupplementalProperties.Count > 0)
                                {
                                    if (telemetryInfo.SupplementalProperties.ContainsKey("unit"))
                                    {
                                        DTUnitInfo unitInfo = telemetryInfo.SupplementalProperties["unit"] as DTUnitInfo;
                                        data.unit = unitInfo.Symbol;
                                    }
                                }

                                data.dataType = telemetryInfo.Schema.EntityKind == DTEntityKind.Integer ? "Long" : "Double";
                                data.TelemetryName = telemetryInfo.Name;
                                //data.TelemetryType = telemetryInfo.Schema;
                                deviceData.telemetry.Add(data);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error {ex}");
            }

            return Json(deviceData);
        }

        [HttpGet]
        public async Task<ActionResult> GetCommand(string modelid)
        {
            List<COMMAND_DATA> commandData = new List<COMMAND_DATA>();

            try
            {
                var dtmiContent = await Resolve(modelid);

                if (!string.IsNullOrEmpty(dtmiContent))
                {
                    ModelParser parser = new ModelParser();
                    parser.DtmiResolver = DtmiResolver;
                    var parsedDtmis = await parser.ParseAsync(new List<string> { dtmiContent });

                    var interfaces = parsedDtmis.Where(r => r.Value.EntityKind == DTEntityKind.Command).ToList();

                    foreach (var dt in interfaces)
                    {
                        COMMAND_DATA data = new COMMAND_DATA();

                        DTCommandInfo commandInfo = dt.Value as DTCommandInfo;

                        if (commandInfo.DisplayName.Count > 0)
                        {
                            data.CommandDisplayName = commandInfo.DisplayName["en"];
                        }

                        if (commandInfo.Description.Count > 0)
                        {
                            data.CommandDescription = commandInfo.Description["en"];
                        }

                        data.CommandName = commandInfo.Name;

                        if (commandInfo.Request != null)
                        {
                            data.requestName = commandInfo.Request.Name;
                            data.requestKind = commandInfo.Request.Schema.EntityKind.ToString() ;
                        }

                        commandData.Add(data);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error {ex}");
            }

            return Json(commandData);
        }

        [HttpPost]
        public bool SendCommand(string deviceid, string command, string parameter)
        {

            _logger.LogInformation($"SendCommand");
            return true;
        }


        // https://docs.microsoft.com/en-us/dotnet/api/microsoft.azure.devices.registrymanager?view=azure-dotnet
        [HttpPost]
        public async Task<bool> AddDevice(string deviceId)
        {

            return await _helper.AddDevice(deviceId);
        }

        [HttpPost]
        public async Task<Twin> SetModelId(string connectionString, string modelId)
        {
            return await _helper.SetModelId(connectionString, modelId);
        }

        [HttpPost]
        public async Task<ActionResult> ConnectDevice(string connectionString, string modelId)
        {
            Twin twin = await _helper.ConnectDevice(connectionString, modelId);
            return Json(twin.ToString());
        }

        [HttpPost]
        public async Task<ActionResult> SendTelemetry(string connectionString, string modelId)
        {
            
            Twin twin = await _helper.SendTelemetry(connectionString, modelId);
            return Json(twin.ToString());
        }

        [HttpGet]
        public async Task<ActionResult> GetTwin(string deviceId)
        {

            Twin twin = await _helper.GetTwin(deviceId);
            JObject twinJson = (JObject)JsonConvert.DeserializeObject(twin.ToJson());
            return Json(twinJson.ToString());
        }

        [HttpDelete]
        public async Task<bool> DeleteDevice(string deviceId)
        {
            return await _helper.DeleteDevice(deviceId);
        }
    }
}
