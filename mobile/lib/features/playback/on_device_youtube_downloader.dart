import 'dart:async';
import 'dart:io';
import 'dart:math';

import 'package:flutter/foundation.dart';
import 'package:youtube_explode_dart/youtube_explode_dart.dart';

import '../../core/api/api_client.dart';
import 'local_audio.dart';

class OnDeviceDownloadCancelToken {
  bool _canceled = false;
  final List<VoidCallback> _listeners = [];

  bool get isCanceled => _canceled;

  void addListener(VoidCallback listener) {
    if (_canceled) {
      listener();
    } else {
      _listeners.add(listener);
    }
  }

  void cancel() {
    if (_canceled) return;
    _canceled = true;
    for (final l in List<VoidCallback>.from(_listeners)) {
      try {
        l();
      } catch (_) {}
    }
    _listeners.clear();
  }
}

class OnDeviceAudioResult {
  const OnDeviceAudioResult({
    required this.tempFile,
    required this.extension,
    required this.sizeBytes,
    required this.bitrateKbps,
  });

  final File tempFile;
  final String extension;
  final int sizeBytes;
  final int bitrateKbps;
}

abstract interface class OnDeviceYouTubeDownloader {
  bool get isEnabled;

  Future<OnDeviceAudioResult> downloadAudio({
    required String youtubeUrl,
    required Directory workDir,
    int? preferredBitrateKbps,
    void Function(int percent)? onProgress,
    OnDeviceDownloadCancelToken? cancelToken,
  });
}

/// Pure-Dart on-device YouTube audio-only downloader using the device's own
/// residential/mobile network connection (zero cloud extraction cost).
class YoutubeExplodeOnDeviceDownloader implements OnDeviceYouTubeDownloader {
  YoutubeExplodeOnDeviceDownloader({bool? enabled})
    : _enabled = enabled ?? !Platform.environment.containsKey('FLUTTER_TEST');

  static const int maxAudioBytes = 100 * 1024 * 1024;
  static const Duration _manifestTimeout = Duration(seconds: 25);
  static const Duration _chunkTimeout = Duration(seconds: 25);

  /// visionOS InnerTube client matching yt-dlp's working audio-only profile.
  /// Must keep WatchPage enabled (default `requireWatchPage: true`) so YouTube
  /// receives valid visitor context and serves full audio streams without 403.
  static const YoutubeApiClient visionOsClient = YoutubeApiClient(
    {
      'context': {
        'client': {
          'clientName': 'VISIONOS',
          'clientVersion': '1.02',
          'deviceMake': 'Apple',
          'deviceModel': 'RealityDevice17,1',
          'userAgent':
              'Mozilla/5.0 (Macintosh; Intel Mac OS X 15_7_3) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.0 Safari/605.1.15',
          'osName': 'visionOS',
          'osVersion': '26.5.23O471',
          'hl': 'en',
          'gl': 'US',
          'timeZone': 'UTC',
          'utcOffsetMinutes': 0,
        },
      },
    },
    'https://www.youtube.com/youtubei/v1/player?prettyPrint=false',
    headers: {
      'X-YouTube-Client-Name': '101',
      'X-YouTube-Client-Version': '1.02',
      'User-Agent':
          'Mozilla/5.0 (Macintosh; Intel Mac OS X 15_7_3) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.0 Safari/605.1.15',
    },
  );

  final bool _enabled;

  @override
  bool get isEnabled => _enabled;

  static AudioOnlyStreamInfo? selectBestAudioStream(
    List<AudioOnlyStreamInfo> streams, {
    int? preferredBitrateKbps,
    bool? requireMp4Only,
  }) {
    final mp4Only = requireMp4Only ?? Platform.isIOS;
    final valid = streams
        .where(
          (s) =>
              s.size.totalBytes > 16 && s.size.totalBytes <= maxAudioBytes,
        )
        .toList();
    if (valid.isEmpty) return null;

    // Prefer MP4/M4A (AAC) for universal native playback on Android (ExoPlayer) & iOS (AVPlayer).
    // On iOS, AVPlayer does not decode WebM/Opus natively, so require MP4/M4A.
    final mp4 = valid
        .where((s) => s.container == StreamContainer.mp4)
        .toList()
      ..sort((a, b) => a.bitrate.compareTo(b.bitrate));

    if (mp4Only && mp4.isEmpty) return null;

    final pool = mp4.isNotEmpty
        ? mp4
        : (List<AudioOnlyStreamInfo>.from(valid)
            ..sort((a, b) => a.bitrate.compareTo(b.bitrate)));

    if (preferredBitrateKbps != null && preferredBitrateKbps <= 128) {
      final standard = pool
          .where((s) => s.bitrate.kiloBitsPerSecond <= 165)
          .toList();
      if (standard.isNotEmpty) return standard.last;
    }
    return pool.last;
  }

  static ApiFailure classifyError(Object error) {
    if (error is ApiFailure) return error;
    final text = error.toString().toLowerCase();
    if (text.contains('canceled') || text.contains('cancelled')) {
      return const ApiFailure('AUDIO_DOWNLOAD_CANCELED', status: 499);
    }
    if (text.contains('sign in to confirm') ||
        text.contains('not a bot') ||
        text.contains('bot')) {
      return const ApiFailure('SOURCE_ACCESS_CHECK_REQUIRED', status: 422);
    }
    if (text.contains('403') || text.contains('forbidden')) {
      return const ApiFailure('SOURCE_ACCESS_FORBIDDEN', status: 422);
    }
    if (text.contains('429') || text.contains('too many requests')) {
      return const ApiFailure('RATE_LIMITED', status: 429);
    }
    if (error is VideoUnavailableException ||
        error is VideoUnplayableException ||
        text.contains('private') ||
        text.contains('unavailable') ||
        text.contains('members-only') ||
        text.contains('age')) {
      return const ApiFailure('SOURCE_RESTRICTED_OR_UNSUPPORTED', status: 422);
    }
    if (error is SocketException ||
        error is TimeoutException ||
        error is HttpException ||
        text.contains('timed out') ||
        text.contains('connection reset')) {
      return const ApiFailure('SOURCE_NETWORK_ERROR', status: 503);
    }
    return const ApiFailure('AUDIO_EXTRACTION_FAILED', status: 503);
  }

  @override
  Future<OnDeviceAudioResult> downloadAudio({
    required String youtubeUrl,
    required Directory workDir,
    int? preferredBitrateKbps,
    void Function(int percent)? onProgress,
    OnDeviceDownloadCancelToken? cancelToken,
  }) async {
    if (!isEnabled) {
      throw const ApiFailure('AUDIO_DOWNLOAD_UNAVAILABLE', status: 503);
    }
    final uri = Uri.tryParse(youtubeUrl.trim());
    if (uri == null ||
        uri.scheme != 'https' ||
        ![
          'youtube.com',
          'www.youtube.com',
          'm.youtube.com',
          'music.youtube.com',
          'youtu.be',
        ].contains(uri.host.toLowerCase())) {
      throw const ApiFailure('INVALID_SOURCE_URL', status: 400);
    }

    VideoId videoId;
    try {
      videoId = VideoId(youtubeUrl.trim());
    } catch (_) {
      throw const ApiFailure('INVALID_SOURCE_URL', status: 400);
    }

    final yt = YoutubeExplode();
    var ytClosed = false;
    void closeYt() {
      if (!ytClosed) {
        ytClosed = true;
        yt.close();
      }
    }

    cancelToken?.addListener(closeYt);
    File? partFile;
    try {
      _throwIfCanceled(cancelToken);
      onProgress?.call(5);

      const clientProfiles = <List<YoutubeApiClient>?>[
        [visionOsClient],
        [YoutubeApiClient.androidSdkless],
      ];

      Object? lastError;
      for (final clients in clientProfiles) {
        _throwIfCanceled(cancelToken);
        try {
          final manifest = await yt.videos.streamsClient
              .getManifest(videoId, ytClients: clients)
              .timeout(_manifestTimeout);
          _throwIfCanceled(cancelToken);

          final chosen = selectBestAudioStream(
            manifest.audioOnly.toList(),
            preferredBitrateKbps: preferredBitrateKbps,
          );
          if (chosen == null) {
            throw const ApiFailure('SOURCE_AUDIO_UNAVAILABLE', status: 422);
          }

          final ext = chosen.container == StreamContainer.mp4
              ? 'm4a'
              : chosen.container.name.toLowerCase();
          final totalBytes = chosen.size.totalBytes;
          if (totalBytes <= 16 || totalBytes > maxAudioBytes) {
            throw const ApiFailure(
              'SOURCE_RESTRICTED_OR_UNSUPPORTED',
              status: 422,
            );
          }

          await workDir.create(recursive: true);
          final tokenHex = List.generate(
            8,
            (_) =>
                Random.secure().nextInt(256).toRadixString(16).padLeft(2, '0'),
          ).join();
          partFile = File('${workDir.path}/yt_dl_$tokenHex.$ext.part');

          await _streamToFile(
            yt: yt,
            streamInfo: chosen,
            totalBytes: totalBytes,
            destination: partFile,
            onProgress: onProgress,
            cancelToken: cancelToken,
          );

          _throwIfCanceled(cancelToken);
          final actualLength = await partFile.length();
          if (actualLength <= 16 ||
              actualLength > maxAudioBytes ||
              actualLength < (totalBytes * 0.98).floor()) {
            throw const ApiFailure('AUDIO_FILE_INVALID', status: 422);
          }
          final raf = await partFile.open();
          Uint8List header;
          try {
            header = await raf.read(16);
          } finally {
            await raf.close();
          }
          if (!LocalLibrary.isValidAudioHeader(header, ext)) {
            throw const ApiFailure('AUDIO_FILE_INVALID', status: 422);
          }

          onProgress?.call(95);
          final result = OnDeviceAudioResult(
            tempFile: partFile,
            extension: ext,
            sizeBytes: actualLength,
            bitrateKbps: chosen.bitrate.kiloBitsPerSecond.round(),
          );
          partFile = null; // Ownership transferred to caller.
          return result;
        } catch (e) {
          if (partFile != null && await partFile.exists()) {
            try {
              await partFile.delete();
            } catch (_) {}
            partFile = null;
          }
          final classified = classifyError(e);
          if (classified.code == 'AUDIO_DOWNLOAD_CANCELED' ||
              classified.code == 'SOURCE_RESTRICTED_OR_UNSUPPORTED' ||
              classified.code == 'RATE_LIMITED') {
            throw classified;
          }
          lastError = classified;
        }
      }

      throw classifyError(
        lastError ?? const ApiFailure('AUDIO_EXTRACTION_FAILED', status: 503),
      );
    } catch (e) {
      if (partFile != null && await partFile.exists()) {
        try {
          await partFile.delete();
        } catch (_) {}
      }
      throw classifyError(e);
    } finally {
      closeYt();
    }
  }

  Future<void> _streamToFile({
    required YoutubeExplode yt,
    required AudioOnlyStreamInfo streamInfo,
    required int totalBytes,
    required File destination,
    void Function(int percent)? onProgress,
    OnDeviceDownloadCancelToken? cancelToken,
  }) async {
    final sink = destination.openWrite();
    var received = 0;
    var lastReported = 5;
    try {
      await for (final chunk
          in yt.videos.streamsClient.get(streamInfo).timeout(_chunkTimeout)) {
        _throwIfCanceled(cancelToken);
        if (chunk.isEmpty) continue;
        received += chunk.length;
        if (received > maxAudioBytes) {
          throw const ApiFailure('AUDIO_FILE_INVALID', status: 422);
        }
        sink.add(chunk);
        final pct = (10 + ((received / totalBytes) * 84)).round().clamp(10, 94);
        if (pct > lastReported) {
          lastReported = pct;
          onProgress?.call(pct);
        }
      }
      await sink.flush();
    } finally {
      await sink.close();
    }
  }

  static void _throwIfCanceled(OnDeviceDownloadCancelToken? token) {
    if (token?.isCanceled == true) {
      throw const ApiFailure('AUDIO_DOWNLOAD_CANCELED', status: 499);
    }
  }
}
