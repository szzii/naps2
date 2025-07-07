// src/service/request/index.js

import axios from 'axios';
import { xml2json } from './xml';

// 创建 Axios 实例
const instance = axios.create();

// 请求拦截器
instance.interceptors.request.use(
  config => {
    // 请求发送前的处理
    return config;
  },
  error => {
    // 请求错误处理
    return Promise.reject(error);
  }
);

// 响应拦截器
instance.interceptors.response.use(
  response => {
    // 处理 2xx 响应
    const contentType = response.headers['content-type'] || response.headers['Content-Type'] || '';
    if (contentType.includes('text/xml') || contentType.includes('application/xml')) {
      const xml = response.data.toString();
      return xml2json(xml);
    }
    return response;
  },
  error => {
    // 处理非 2xx 响应
    return Promise.reject(error);
  }
);

export default instance;
