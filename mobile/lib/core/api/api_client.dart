import 'dart:async';
import 'dart:math';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

const _rawApiBaseUrl = String.fromEnvironment(
  'TMUSIC_API_URL',
  defaultValue: String.fromEnvironment(
    'TMUSIC_API_BASE_URL',
    defaultValue: 'http://10.0.2.2:5078',
  ),
);

// Local-device builds may explicitly opt into a private LAN HTTP endpoint for
// development. Release builds keep HTTPS-only behavior by default.
const _allowInsecureLocalApi = bool.fromEnvironment(
  'TMUSIC_ALLOW_INSECURE_LOCAL_API',
  defaultValue: false,
);

String _normalizeApiOrigin(String raw) {
  var trimmed = raw.trim();
  while (trimmed.endsWith('/')) {
    trimmed = trimmed.substring(0, trimmed.length - 1);
  }
  if (trimmed.endsWith('/api/v1')) {
    trimmed = trimmed.substring(0, trimmed.length - '/api/v1'.length);
  }
  return trimmed;
}

final String apiBaseUrl = _normalizeApiOrigin(_rawApiBaseUrl);

final publicApiProvider = Provider<ApiClient>((ref) {
  final api = ApiClient();
  ref.onDispose(api.close);
  return api;
});

final offlineModeProvider = NotifierProvider<OfflineModeNotifier, bool>(
  OfflineModeNotifier.new,
);

class OfflineModeNotifier extends Notifier<bool> {
  @override
  bool build() => false;
  void setOffline(bool value) {
    if (state != value) state = value;
  }
}

class ApiFailure implements Exception {
  const ApiFailure(this.code, {this.status, this.detail});
  final String code;
  final int? status;
  final String? detail;
  @override
  String toString() => detail ?? code;
}

class ApiClient {
  ApiClient({this.token, Dio? dio})
    : _dio =
          dio ??
          Dio(
            BaseOptions(
              baseUrl: '$apiBaseUrl/api/v1',
              connectTimeout: const Duration(seconds: 12),
              receiveTimeout: const Duration(seconds: 25),
              sendTimeout: const Duration(seconds: 15),
            ),
          ) {
    if (kReleaseMode &&
        Uri.parse(apiBaseUrl).scheme != 'https' &&
        !_allowInsecureLocalApi) {
      throw StateError('Release API must use HTTPS.');
    }
  }

  final Dio _dio;
  final Future<String?> Function()? token;

  Future<Map<String, dynamic>> request(
    String method,
    String path, {
    Map<String, dynamic>? data,
    Map<String, dynamic>? query,
    String? version,
    Map<String, dynamic>? headers,
  }) async {
    final maxAttempts = method.toUpperCase() == 'GET' ? 2 : 1;
    for (var attempt = 1; attempt <= maxAttempts; attempt++) {
      final access = await token?.call();
      if (token != null && access == null) {
        throw const ApiFailure('session_expired', status: 401);
      }
      try {
        final r = await _dio.request<dynamic>(
          path,
          data: data,
          queryParameters: query,
          options: Options(
            method: method,
            headers: {
              'Accept': 'application/json',
              if (access != null) 'Authorization': 'Bearer $access',
              if (version != null && version.isNotEmpty)
                'If-Match': '"$version"',
              ...?headers,
            },
          ),
        );
        return r.data is Map
            ? Map<String, dynamic>.from(r.data as Map)
            : <String, dynamic>{};
      } on DioException catch (e) {
        final status = e.response?.statusCode;
        final transient =
            status == null || status == 502 || status == 503 || status == 504;
        if (attempt < maxAttempts && transient) {
          await Future<void>.delayed(
            Duration(milliseconds: 250 * attempt + Random().nextInt(120)),
          );
          continue;
        }
        throw _mapDioError(e);
      }
    }
    throw const ApiFailure('network_error');
  }

  Future<Map<String, dynamic>> uploadMultipart(
    String path, {
    required String fileName,
    required Uint8List bytes,
    String? version,
  }) async {
    final access = await token?.call();
    if (token != null && access == null) {
      throw const ApiFailure('session_expired', status: 401);
    }
    try {
      final form = FormData.fromMap({
        'file': MultipartFile.fromBytes(bytes, filename: fileName),
      });
      final r = await _dio.post<dynamic>(
        path,
        data: form,
        options: Options(
          headers: {
            'Accept': 'application/json',
            if (access != null) 'Authorization': 'Bearer $access',
            if (version != null && version.isNotEmpty) 'If-Match': '"$version"',
          },
        ),
      );
      return r.data is Map
          ? Map<String, dynamic>.from(r.data as Map)
          : <String, dynamic>{};
    } on DioException catch (e) {
      throw _mapDioError(e);
    }
  }

  Future<Uint8List> fetchBytes(String apiRelativeOrFullPath) async {
    final access = await token?.call();
    if (token != null && access == null) {
      throw const ApiFailure('session_expired', status: 401);
    }
    final normalizedPath = apiRelativeOrFullPath.startsWith('/api/v1/')
        ? apiRelativeOrFullPath.substring('/api/v1'.length)
        : apiRelativeOrFullPath;
    try {
      final r = await _dio.get<List<int>>(
        normalizedPath,
        options: Options(
          responseType: ResponseType.bytes,
          receiveTimeout: const Duration(minutes: 6),
          headers: {if (access != null) 'Authorization': 'Bearer $access'},
        ),
      );
      return Uint8List.fromList(r.data ?? const <int>[]);
    } on DioException catch (e) {
      throw _mapDioError(e);
    }
  }

  Future<Uint8List> postBytes(String path, {Map<String, dynamic>? data}) async {
    final access = await token?.call();
    if (token != null && access == null) {
      throw const ApiFailure('session_expired', status: 401);
    }
    try {
      final r = await _dio.post<List<int>>(
        path,
        data: data,
        options: Options(
          responseType: ResponseType.bytes,
          receiveTimeout: const Duration(minutes: 6),
          headers: {if (access != null) 'Authorization': 'Bearer $access'},
        ),
      );
      return Uint8List.fromList(r.data ?? const <int>[]);
    } on DioException catch (e) {
      throw _mapDioError(e);
    }
  }

  ApiFailure _mapDioError(DioException e) {
    final b = e.response?.data;
    return ApiFailure(
      b is Map ? (b['code'] as String? ?? 'request_failed') : 'network_error',
      status: e.response?.statusCode,
      detail: b is Map ? b['detail'] as String? : null,
    );
  }

  void close() => _dio.close();
}
