import { getScanSettingConfig, getScannerBrightness } from './getCapabilities';
import { getScanSetting } from './serializeScanSetting';
import request from '../utils/request';

class Scanner {
  constructor(opts) {
    this.ip = opts.ip;
    this.port = opts.port || 8080;
    this.version = opts.version || 2.0;
    this.rs = opts.rs || 'eSCL';
    this.protocol = 'http://';
  }

  execute(path, opts = {}) {
    const params = { ...opts };
    params.url = `${this.protocol}${this.ip}:${this.port}/${this.rs}/${path}`;
    return request(params);
  }

  async getDevices() {
    try {
      // path 直接写 Devices，execute 内会拼上 /eSCL/
      const res = await this.execute('Devices', { method: 'GET' });
      return res;  // 返回一个数组
    } catch (error) {
      throw error;
    }
  }

  async ScannerCapabilities(deviceId) {
    try {
      const res = await this.execute('ScannerCapabilities/' + deviceId, { method: 'GET' });
      return {
        capabilities: res,
        scansetting: getScanSettingConfig(res),
        BrightnessSupport: getScannerBrightness(res),
      };
    } catch (error) {
      throw error;
    }
  }

  async ScanJobs(deviceId,params) {
    const payload = getScanSetting({ ...params, Version: this.version });
    const res = await this.execute('ScanJobs?Id=' + deviceId, { method: 'POST', data: payload });
    return res.headers.location;
  }

  async ScannerStatus() {
    try {
      const res = await this.execute('ScannerStatus');
      return res['scan:ScannerStatus'];
    } catch (error) {
      throw error;
    }
  }

  NextDocument(jobId) {
    return this.execute(`ScanJobs/${jobId}/NextDocument`, { responseType: 'arraybuffer' })
      .then(
        data => data,
        err => {
          if (err.response && err.response.status === 503) {
            return new Promise(resolve => setTimeout(resolve, 2000))
              .then(() => this.NextDocument(jobId));
          }
          return Promise.reject(err);
        }
      );
  }

  ScanImageInfo(jobId) {
    return this.execute(`ScanJobs/${jobId}/ScanImageInfo`);
  }
}

export default Scanner;
