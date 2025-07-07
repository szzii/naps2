const { PAPER_PAGE_SIZES, DOCUMENT_FORMATS, SUPPORTED_COLOR_MODES } = require('./const');
const { getArrayCommonItem } = require('../utils');

function getScanRegion(InputCaps) {
  const region = { default_value: '', options: [], name: 'ScanRegions' };
  const maxH = InputCaps['scan:MaxHeight'];
  const minH = InputCaps['scan:MinHeight'];
  const maxW = InputCaps['scan:MaxWidth'];
  const minW = InputCaps['scan:MinWidth'];
  PAPER_PAGE_SIZES.forEach(sz => {
    if (sz.Height > minH && sz.Height < maxH && sz.Width > minW && sz.Width < maxW) {
      region.options.push(sz.label);
    }
  });
  region.default_value = region.options[0] || '';
  return region;
}

function getColorMode(InputCaps) {
  const mode = { default_value: '', options: [], name: 'ColorMode' };
  const caps = InputCaps['scan:SettingProfiles']['scan:SettingProfile']['scan:ColorModes']['scan:ColorMode'];
  mode.options = getArrayCommonItem(SUPPORTED_COLOR_MODES, caps);
  mode.default_value = mode.options[0] || '';
  return mode;
}

function getResolution(InputCaps) {
  const resObj = { default_value: null, options: [], name: 'Resolution' };
  const list = InputCaps['scan:SettingProfiles']['scan:SettingProfile']['scan:SupportedResolutions']['scan:DiscreteResolutions']['scan:DiscreteResolution'];
  list.forEach(d => resObj.options.push(d['scan:XResolution']));
  resObj.default_value = resObj.options[0] || null;
  return resObj;
}

function getDocumentFormat(InputCaps) {
  const doc = { default_value: '', options: [], name: 'DocumentFormat' };
  const list = InputCaps['scan:SettingProfiles']['scan:SettingProfile']['scan:DocumentFormats']['pwg:DocumentFormat'];
  DOCUMENT_FORMATS.forEach(fmt => { if (list.includes(fmt)) doc.options.push(fmt); });
  doc.default_value = doc.options[0] || '';
  return doc;
}

function getScanSettingConfig(capabilities) {
  const setting = { adf: { Simplex: [], Duplex: [], AdfOptions: [], FeederCapacity: 0 }, platen: [] };
  const adf = capabilities['scan:ScannerCapabilities']['scan:Adf'];
  if (adf) {
    const simple = adf['scan:AdfSimplexInputCaps'];
    setting.adf.Simplex = [getScanRegion(simple), getColorMode(simple), getResolution(simple), getDocumentFormat(simple)];
    setting.adf.FeederCapacity = adf['scan:FeederCapacity'];
    if (adf['scan:AdfOptions']) setting.adf.AdfOptions = adf['scan:AdfOptions']['scan:AdfOption'];
    if (adf['scan:AdfDuplexInputCaps']) {
      const dup = adf['scan:AdfDuplexInputCaps'];
      setting.adf.Duplex = [getScanRegion(dup), getColorMode(dup), getResolution(dup), getDocumentFormat(dup)];
    }
  }
  const plat = capabilities['scan:ScannerCapabilities']['scan:Platen'];
  if (plat) {
    const input = plat['scan:PlatenInputCaps'];
    setting.platen = [getScanRegion(input), getColorMode(input), getResolution(input), getDocumentFormat(input)];
  }
  return setting;
}

function getScannerBrightness(capabilities) {
  const b = capabilities['scan:ScannerCapabilities']['scan:BrightnessSupport'];
  return b ? { Min: b['scan:Min'], Max: b['scan:Max'], Step: b['scan:Step'], Normal: b['scan:Normal'] } : null;
}

module.exports = { getScanRegion, getColorMode, getResolution, getDocumentFormat, getScanSettingConfig, getScannerBrightness };

