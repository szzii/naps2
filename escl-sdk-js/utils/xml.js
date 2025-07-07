
const { XMLParser, XMLBuilder } = require('fast-xml-parser');

const defaultOptions = {
  attributeNamePrefix: '_',
  attrNodeName: '',
  textNodeName: '#text',
  ignoreAttributes: false,
  cdataTagName: '__cdata',
  cdataPositionChar: '\\c',
  format: false,
  indentBy: '  ',
  supressEmptyNode: false,
};

const xmlParser = new XMLParser(defaultOptions);
const xmlBuilder = new XMLBuilder(defaultOptions);

function json2xml(json) {
  return xmlBuilder.build(json);
}

function xml2json(xml) {
  return xmlParser.parse(xml);
}

module.exports = {
  json2xml,
  xml2json,
};
