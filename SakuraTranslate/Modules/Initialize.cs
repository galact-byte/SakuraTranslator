using SakuraTranslate.Helpers;
using SakuraTranslate.Models;
using SimpleJSON;
using System;
using System.Collections.Generic;
using System.Net;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;
using XUnity.AutoTranslator.Plugin.Core.Utilities;
using XUnity.Common.Logging;

namespace SakuraTranslate
{
    public partial class SakuraTranslateEndpoint : ITranslateEndpoint
    {
        private static string NormalizeEndpoint(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("Endpoint 必须是有效的 HTTP/HTTPS 地址，例如 http://127.0.0.1:8080/v1/chat/completions。");
            var builder = new UriBuilder(uri);
            string path = builder.Path.TrimEnd('/');
            if (path.Length == 0) path = "/v1/chat/completions";
            else if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) path += "/chat/completions";
            builder.Path = path;
            return builder.Uri.AbsoluteUri;
        }

        public void Initialize(IInitializationContext context)
        {
            _endpoint = NormalizeEndpoint(context.GetOrCreateSetting<string>("Sakura", "Endpoint", "http://127.0.0.1:8080/v1/chat/completions"));
            _modelName = context.GetOrCreateSetting<string>("Sakura", "ModelName", "Auto").Trim();
            _modelVersion = context.GetOrCreateSetting<string>("Sakura", "ModelVersion", "Auto").Trim();
            _autoDetectModel = string.Equals(_modelName, "Auto", StringComparison.OrdinalIgnoreCase);
            if (_autoDetectModel)
            {
                var uri = new UriBuilder(_endpoint);
                const string completionPath = "/chat/completions";
                if (!uri.Path.EndsWith(completionPath, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("自动识别需要 /v1/chat/completions 兼容接口；自定义接口请手动填写 ModelName 与 ModelVersion。");
                uri.Path = uri.Path.Substring(0, uri.Path.Length - completionPath.Length) + "/models";
                _modelsEndpoint = uri.Uri.AbsoluteUri;
            }
            else _modelType = TranslateModelHelper.GetTranslationModel(_modelName, _modelVersion);
            int timeoutSeconds;
            if (!int.TryParse(context.GetOrCreateSetting<string>("Sakura", "RequestTimeoutSeconds", "100"), out timeoutSeconds)
                || timeoutSeconds <= 0 || timeoutSeconds > int.MaxValue / 1000)
                throw new ArgumentException("RequestTimeoutSeconds 必须是大于零的有效整数秒数。");
            _requestTimeoutMs = timeoutSeconds * 1000;
            #region maxTokens
            try
            {
                _maxTokensMode = (MaxTokensMode)Enum.Parse(typeof(MaxTokensMode), context.GetOrCreateSetting<string>("Sakura", "MaxTokensMode", "Static"), true);
            }
            catch (Exception ex)
            {
                XuaLogger.AutoTranslator.Warn(ex, $"SakuraTranslate.Initialize: Failed to parse max tokens mode: {context.GetOrCreateSetting<string>("Sakura", "MaxTokensMode", "Static")}, falling back to Static");
                _maxTokensMode = MaxTokensMode.Static;
            }
            if (!int.TryParse(context.GetOrCreateSetting<string>("Sakura", "StaticMaxTokens", "512"), out _staticMaxTokens)) { _staticMaxTokens = 512; }
            if (!double.TryParse(context.GetOrCreateSetting<string>("Sakura", "DynamicMaxTokensMultiplier", "1.5"), out _dynamicMaxTokensMultiplier)) { _dynamicMaxTokensMultiplier = 1.5; }
            #endregion
            // init dict
            #region init dict
            try
            {
                _dictMode = (DictMode)Enum.Parse(typeof(DictMode), context.GetOrCreateSetting<string>("Sakura", "DictMode", "None"), true);
            }
            catch (Exception ex)
            {
                XuaLogger.AutoTranslator.Warn(ex, $"SakuraTranslate.Initialize: Failed to parse dict mode: {context.GetOrCreateSetting<string>("Sakura", "DictMode", "None")}, falling back to None");
                _dictMode = DictMode.None;
            }
            var dictStr = context.GetOrCreateSetting<string>("Sakura", "Dict", string.Empty);
            if (string.IsNullOrEmpty(dictStr))
            {
                XuaLogger.AutoTranslator.Warn("SakuraTranslate.Initialize: Dict is empty, setting DictMode to None");
                _dictMode = DictMode.None;
                _fullDictStr = string.Empty;
            }
            else
            {
                try
                {
                    _dict = new Dictionary<string, List<string>>();
                    var dictJObj = JSON.Parse(dictStr);
                    foreach (var item in dictJObj)
                    {
                        try
                        {
                            var vArr = JSON.Parse(item.Value.ToString()).AsArray;
                            List<string> vList;
                            if (vArr.Count <= 0)
                            {
                                throw new Exception();
                            }
                            else if (vArr.Count == 1)
                            {
                                vList = new List<string> { JsonHelper.Unescape(vArr[0].ToString().Trim('\"')), string.Empty };
                            }
                            else
                            {
                                vList = new List<string> { JsonHelper.Unescape(vArr[0].ToString().Trim('\"')),
                                    JsonHelper.Unescape(vArr[1].ToString().Trim('\"')) };
                            }
                            _dict.Add(JsonHelper.Unescape(item.Key.Trim('\"')), vList);
                        }
                        catch
                        {
                            _dict.Add(JsonHelper.Unescape(item.Key.Trim('\"')),
                                new List<string> { JsonHelper.Unescape(item.Value.ToString().Trim('\"')), string.Empty });
                        }
                    }
                    if (_dict.Count == 0)
                    {
                        _dictMode = DictMode.None;
                        _fullDictStr = string.Empty;
                    }
                    else
                    {
                        _fullDictStr = GetDictStr(_dict);
                    }
                }
                catch (Exception ex)
                {
                    XuaLogger.AutoTranslator.Warn(ex, $"SakuraTranslate.Initialize: Failed to parse dict string: {dictStr}");
                    _dictMode = DictMode.None;
                    _fullDictStr = string.Empty;
                }
            }
            #endregion
            if (!bool.TryParse(context.GetOrCreateSetting<string>("Sakura", "FixDegeneration", "False"), out _fixDegeneration)) { _fixDegeneration = false; }
            if (!int.TryParse(context.GetOrCreateSetting<string>("Sakura", "MaxConcurrency", "1"), out _maxConcurrency)) { _maxConcurrency = 1; }
            if (_maxConcurrency > ServicePointManager.DefaultConnectionLimit)
            {
                XuaLogger.AutoTranslator.Info($"SakuraTranslate.Initialize: Setting ServicePointManager.DefaultConnectionLimit to {_maxConcurrency}");
                ServicePointManager.DefaultConnectionLimit = _maxConcurrency;
            }
            if (!bool.TryParse(context.GetOrCreateSetting<string>("Sakura", "EnableShortDelay", "False"), out _enableShortDelay)) { _enableShortDelay = false; }
            if (_enableShortDelay)
            {
                XuaLogger.AutoTranslator.Warn("SakuraTranslate.Initialize: Setting translation delay to 0.1s");
                context.SetTranslationDelay(0.1f);
            }
            if (!bool.TryParse(context.GetOrCreateSetting<string>("Sakura", "DisableSpamChecks", "False"), out _disableSpamChecks)) { _disableSpamChecks = false; }
            if (_disableSpamChecks)
            {
                XuaLogger.AutoTranslator.Warn("SakuraTranslate.Initialize: Spam checks are disabled");
                context.DisableSpamChecks();
            }
            // temperature and top_p override settings
            var temperatureOverrideStr = context.GetOrCreateSetting<string>("Sakura", "TemperatureOverride", string.Empty);
            if (!string.IsNullOrEmpty(temperatureOverrideStr))
            {
                if (double.TryParse(temperatureOverrideStr, out double tempOverride))
                {
                    _enableTemperatureOverride = true;
                    _temperatureOverride = tempOverride;
                    XuaLogger.AutoTranslator.Info($"SakuraTranslate.Initialize: TemperatureOverride set to {_temperatureOverride}");
                }
                else
                {
                    _enableTemperatureOverride = false;
                    XuaLogger.AutoTranslator.Warn($"SakuraTranslate.Initialize: Failed to parse TemperatureOverride: {temperatureOverrideStr}");
                }
            }
            else
            {
                _enableTemperatureOverride = false;
            }
            var topPOverrideStr = context.GetOrCreateSetting<string>("Sakura", "TopPOverride", string.Empty);
            if (!string.IsNullOrEmpty(topPOverrideStr))
            {
                if (double.TryParse(topPOverrideStr, out double topPOverride))
                {
                    _enableTopPOverride = true;
                    _topPOverride = topPOverride;
                    XuaLogger.AutoTranslator.Info($"SakuraTranslate.Initialize: TopPOverride set to {_topPOverride}");
                }
                else
                {
                    _enableTopPOverride = false;
                    XuaLogger.AutoTranslator.Warn($"SakuraTranslate.Initialize: Failed to parse TopPOverride: {topPOverrideStr}");
                }
            }
            else
            {
                _enableTopPOverride = false;
            }
            if (!bool.TryParse(context.GetOrCreateSetting<string>("Sakura", "Debug", "False"), out _debug)) { _debug = false; }
        }
    }
}