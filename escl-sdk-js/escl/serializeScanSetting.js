const { PAPER_PAGE_SIZES, XMLNS_PWG, XMLNS_SCAN, XMLNS_DEST, SCAN_SETTING_KEY_MAP } = require('./const');
const { json2xml } = require('../utils/xml');

function getSettingResolution(resolution) {
  return resolution;
}

function getSettingPaperSize(paperType) {
  const paperDef = PAPER_PAGE_SIZES.find(p => p.label === paperType) || PAPER_PAGE_SIZES[0];
  const paper = {
    'pwg:Height': paperDef.Height,
    'pwg:Width': paperDef.Width,
    'pwg:XOffset': paperDef.XOffset,
    'pwg:YOffset': paperDef.YOffset,
  };
  return { [SCAN_SETTING_KEY_MAP.ScanRegion]: paper };
}

function getSettingInputSource(input) {
  return input === 'ADF' ? 'Feeder' : 'Platen';
}

function getSettingIntent(format) {
  return format === 'application/pdf' ? 'Document' : 'Photo';
}

function getHttpDest(httpDest) {
  const result = {};
  Object.keys(httpDest).forEach(key => {
    if (key === 'HttpHeaders') {
      result[SCAN_SETTING_KEY_MAP.HttpHeaders] = {
        [SCAN_SETTING_KEY_MAP.HttpHeader]: httpDest[key]
      };
    } else if (key === 'RetryInfo') {
      result[SCAN_SETTING_KEY_MAP.RetryInfo] = {
        [SCAN_SETTING_KEY_MAP.NumberOfRetries]: httpDest.RetryInfo.NumberOfRetries,
        [SCAN_SETTING_KEY_MAP.RetryInterval]: httpDest.RetryInfo.RetryInterval,
        [SCAN_SETTING_KEY_MAP.RetryTimeOut]: httpDest.RetryInfo.RetryTimeOut
      };
    } else {
      result[SCAN_SETTING_KEY_MAP[key]] = httpDest[key];
    }
  });
  return result;
}

function getDestinations(destSettings) {
  if (Array.isArray(destSettings)) {
    return {
      [SCAN_SETTING_KEY_MAP.ScanDestinations]: destSettings.map(d => ({
        [SCAN_SETTING_KEY_MAP.HttpDestination]: getHttpDest(d)
      }))
    };
  }
  return {
    [SCAN_SETTING_KEY_MAP.ScanDestinations]: {
      [SCAN_SETTING_KEY_MAP.HttpDestination]: getHttpDest(destSettings)
    }
  };
}

function getScanSettingObj(setting) {
  const config = {
    '_xmlns:scan': XMLNS_SCAN,
    '_xmlns:pwg': XMLNS_PWG,
    '_xmlns:dest': XMLNS_DEST
  };

  Object.keys(setting).forEach(key => {
    const value = setting[key];
    switch (key) {
      case 'DocumentFormat':
        config[SCAN_SETTING_KEY_MAP.Intent] = getSettingIntent(value);
        if (setting.Version < 2.1) {
          config[SCAN_SETTING_KEY_MAP.DocumentFormat] = value;
        } else {
          config[SCAN_SETTING_KEY_MAP.DocumentFormatExt] = value;
        }
        break;
      case 'InputSource':
        config[SCAN_SETTING_KEY_MAP.InputSource] = getSettingInputSource(value);
        break;
      case 'ScanRegions':
        Object.assign(config, getSettingPaperSize(value));
        break;
      case 'ScanDestinations':
        Object.assign(config, getDestinations(value));
        break;
      case 'Resolution':
        // handled below
        break;
      default:
        config[SCAN_SETTING_KEY_MAP[key]] = value;
    }
  });

  const res = getSettingResolution(setting.Resolution);
  config[SCAN_SETTING_KEY_MAP.XResolution] = res;
  config[SCAN_SETTING_KEY_MAP.YResolution] = res;

  return { 'scan:ScanSettings': config };
}

function getScanSetting(setting) {
  const obj = getScanSettingObj(setting);
  return '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' + json2xml(obj);
}

module.exports = {
  getSettingResolution,
  getSettingPaperSize,
  getSettingInputSource,
  getSettingIntent,
  getHttpDest,
  getDestinations,
  getScanSettingObj,
  getScanSetting
};

