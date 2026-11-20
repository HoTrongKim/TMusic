import 'dart:async';
import 'dart:io';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:tmusic/core/api/api_client.dart';
import 'package:tmusic/features/playback/local_audio.dart';
import 'package:tmusic/features/playback/on_device_download_manager.dart';
import 'package:tmusic/features/playback/on_device_youtube_downloader.dart';
import 'package:tmusic/shared/models/library_models.dart';

void main() {
  test('LocalLibrary validates MP3, M4A, and WebM headers and rejects HTML', () {
    final mp3Id3 = Uint8List.fromList([0x49, 0x44, 0x33, 0x04, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
    final mp3Frame = Uint8List.fromList([0xFF, 0xFB, 0x90, 0x44, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
    final m4aFtyp = Uint8List.fromList([0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x4D, 0x34, 0x41, 0x20, 0, 0, 0, 0]);
    final webmEbml = Uint8List.fromList([0x1A, 0x45, 0xDF, 0xA3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
    final html = Uint8List.fromList('<!DOCTYPE html><html>'.codeUnits);

    expect(LocalLibrary.isValidAudioHeader(mp3Id3, 'mp3'), isTrue);
    expect(LocalLibrary.isValidAudioHeader(mp3Frame, 'mp3'), isTrue);
    expect(LocalLibrary.isValidAudioHeader(m4aFtyp, 'm4a'), isTrue);
    expect(LocalLibrary.isValidAudioHeader(webmEbml, 'webm'), isTrue);
    expect(LocalLibrary.isValidAudioHeader(html, 'm4a'), isFalse);
    expect(LocalLibrary.isValidAudioHeader(html, 'mp3'), isFalse);
  });

  test('On-device YouTube error classifier maps bot, forbidden, rate-limit, network, and cancel errors', () {
    expect(
      YoutubeExplodeOnDeviceDownloader.classifyError(
        Exception("Sign in to confirm you're not a bot"),
      ).code,
      'SOURCE_ACCESS_CHECK_REQUIRED',
    );
    expect(
      YoutubeExplodeOnDeviceDownloader.classifyError(
        Exception('HTTP 403 Forbidden'),
      ).code,
      'SOURCE_ACCESS_FORBIDDEN',
    );
    expect(
      YoutubeExplodeOnDeviceDownloader.classifyError(
        Exception('HTTP 429 Too Many Requests'),
      ).code,
      'RATE_LIMITED',
    );
    expect(
      YoutubeExplodeOnDeviceDownloader.classifyError(
        const SocketException('Connection reset by peer'),
      ).code,
      'SOURCE_NETWORK_ERROR',
    );
    expect(
      YoutubeExplodeOnDeviceDownloader.classifyError(
        const ApiFailure('AUDIO_DOWNLOAD_CANCELED', status: 499),
      ).code,
      'AUDIO_DOWNLOAD_CANCELED',
    );
  });

  test('OnDeviceDownloadManager isolates jobs per account, reports progress, and supports cancel & retry', () async {
    final downloader = _StubOnDeviceDownloader();
    final manager = OnDeviceDownloadManager(downloader: downloader);
    addTearDown(manager.dispose);

    final songA = Song(
      id: 'song-a',
      version: '1',
      title: 'Bài hát YouTube A',
      customTitle: 'Bài hát YouTube A',
      provider: 'youtube',
      providerContentId: 'aqz-KE-bpKQ',
      url: 'https://www.youtube.com/watch?v=aqz-KE-bpKQ',
      originalUrl: 'https://www.youtube.com/watch?v=aqz-KE-bpKQ',
      capability: 'EMBED_SUPPORTED',
    );

    final updates = <AudioDownloadJob>[];
    final sub = manager.updates.listen((e) {
      if (e.ownerKey == 'user-a') updates.add(e.job);
    });
    addTearDown(sub.cancel);

    final started = await manager.startYouTubeDownload(
      ownerKey: 'user-a',
      song: songA,
      openLocalLibrary: (_) async => LocalLibrary(null, null),
      bitrateKbps: 128,
    );
    expect(OnDeviceDownloadManager.isLocalJobId(started.id), isTrue);
    expect(manager.jobsForOwner('user-a').length, 1);
    expect(manager.jobsForOwner('user-b'), isEmpty);

    // Deduplication returns the active job instead of spawning a duplicate
    final duplicate = await manager.startYouTubeDownload(
      ownerKey: 'user-a',
      song: songA,
      openLocalLibrary: (_) async => LocalLibrary(null, null),
      bitrateKbps: 128,
    );
    expect(duplicate.id, started.id);

    downloader.releaseSuccess();
    await Future<void>.delayed(const Duration(milliseconds: 50));

    final finished = manager.findJob('user-a', started.id);
    expect(finished?.status, 'COMPLETE');
    expect(finished?.progressPercent, 100);
    expect(finished?.audioReady, isTrue);
    expect(updates.any((u) => u.progressPercent == 55), isTrue);

    // Failure & retry flow
    final failingDownloader = _StubOnDeviceDownloader(failWith: const ApiFailure('SOURCE_NETWORK_ERROR', status: 503));
    final manager2 = OnDeviceDownloadManager(downloader: failingDownloader);
    addTearDown(manager2.dispose);
    final failedStart = await manager2.startYouTubeDownload(
      ownerKey: 'user-a',
      song: songA,
      openLocalLibrary: (_) async => LocalLibrary(null, null),
    );
    failingDownloader.releaseFailure();
    await Future<void>.delayed(const Duration(milliseconds: 50));
    final failed = manager2.findJob('user-a', failedStart.id);
    expect(failed?.status, 'FAILED');
    expect(failed?.errorCode, 'SOURCE_NETWORK_ERROR');
    expect(failed?.isRetryable, isTrue);

    // Cancel flow
    final slowDownloader = _StubOnDeviceDownloader();
    final manager3 = OnDeviceDownloadManager(downloader: slowDownloader);
    addTearDown(manager3.dispose);
    final active = await manager3.startYouTubeDownload(
      ownerKey: 'user-a',
      song: songA,
      openLocalLibrary: (_) async => LocalLibrary(null, null),
    );
    await manager3.cancelOrDelete('user-a', active.id);
    expect(slowDownloader.lastToken?.isCanceled, isTrue);
    expect(manager3.findJob('user-a', active.id)?.status, 'CANCELED');
  });
}

class _StubOnDeviceDownloader implements OnDeviceYouTubeDownloader {
  _StubOnDeviceDownloader({this.failWith});
  final ApiFailure? failWith;
  final Completer<void> _gate = Completer<void>();
  OnDeviceDownloadCancelToken? lastToken;

  @override
  bool get isEnabled => true;

  void releaseSuccess() {
    if (!_gate.isCompleted) _gate.complete();
  }

  void releaseFailure() {
    if (!_gate.isCompleted) _gate.complete();
  }

  @override
  Future<OnDeviceAudioResult> downloadAudio({
    required String youtubeUrl,
    required Directory workDir,
    int? preferredBitrateKbps,
    void Function(int percent)? onProgress,
    OnDeviceDownloadCancelToken? cancelToken,
  }) async {
    lastToken = cancelToken;
    onProgress?.call(55);
    await _gate.future;
    if (cancelToken?.isCanceled == true) {
      throw const ApiFailure('AUDIO_DOWNLOAD_CANCELED', status: 499);
    }
    if (failWith != null) throw failWith!;
    final tempDir = await Directory.systemTemp.createTemp('tmusic_stub_yt_');
    final file = File('${tempDir.path}${Platform.pathSeparator}sample.m4a');
    await file.writeAsBytes([
      0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70,
      0x4D, 0x34, 0x41, 0x20, 0x00, 0x00, 0x00, 0x00,
      0x00, 0x00, 0x00, 0x00,
    ]);
    return OnDeviceAudioResult(
      tempFile: file,
      extension: 'm4a',
      sizeBytes: 20,
      bitrateKbps: 128,
    );
  }
}
