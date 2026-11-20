import 'dart:async';
import 'dart:io';
import 'dart:math';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../shared/models/library_models.dart';
import 'local_audio.dart';
import 'on_device_youtube_downloader.dart';

final onDeviceYouTubeDownloaderProvider = Provider<OnDeviceYouTubeDownloader>(
  (ref) => YoutubeExplodeOnDeviceDownloader(),
);

final onDeviceDownloadManagerProvider = Provider<OnDeviceDownloadManager>((
  ref,
) {
  final manager = OnDeviceDownloadManager(
    downloader: ref.watch(onDeviceYouTubeDownloaderProvider),
  );
  ref.onDispose(manager.dispose);
  return manager;
});

class _TrackedLocalJob {
  _TrackedLocalJob({
    required this.job,
    required this.song,
    required this.cancelToken,
    this.bitrateKbps,
    this.onGuestStateChanged,
    this.onCompleted,
  });

  AudioDownloadJob job;
  final Song song;
  final OnDeviceDownloadCancelToken cancelToken;
  final int? bitrateKbps;
  final Future<void> Function(String status, {bool? audioReady})?
  onGuestStateChanged;
  final void Function()? onCompleted;
}

class OnDeviceDownloadManager {
  OnDeviceDownloadManager({required this.downloader});

  static const String jobPrefix = 'local-yt-';

  final OnDeviceYouTubeDownloader downloader;
  final Map<String, Map<String, _TrackedLocalJob>> _jobsByOwner = {};
  final StreamController<({String ownerKey, AudioDownloadJob job})> _updates =
      StreamController<({String ownerKey, AudioDownloadJob job})>.broadcast();

  Stream<({String ownerKey, AudioDownloadJob job})> get updates =>
      _updates.stream;

  bool get isEnabled => downloader.isEnabled;

  static bool isLocalJobId(String jobId) => jobId.startsWith(jobPrefix);

  List<AudioDownloadJob> jobsForOwner(String ownerKey) {
    final map = _jobsByOwner[ownerKey];
    if (map == null) return const [];
    return map.values.map((e) => e.job).toList().reversed.toList();
  }

  AudioDownloadJob? findJob(String ownerKey, String jobId) =>
      _jobsByOwner[ownerKey]?[jobId]?.job;

  Future<AudioDownloadJob> startYouTubeDownload({
    required String ownerKey,
    required Song song,
    required Future<LocalLibrary> Function(String owner) openLocalLibrary,
    int? bitrateKbps,
    Future<void> Function(String status, {bool? audioReady})?
    onGuestStateChanged,
    void Function()? onCompleted,
  }) async {
    final ownerJobs = _jobsByOwner.putIfAbsent(ownerKey, () => {});
    for (final existing in ownerJobs.values) {
      if (existing.song.id == song.id && existing.job.isActive) {
        return existing.job;
      }
    }

    final suffix = List.generate(
      8,
      (_) => Random.secure().nextInt(256).toRadixString(16).padLeft(2, '0'),
    ).join();
    final jobId = '$jobPrefix$suffix';
    final token = OnDeviceDownloadCancelToken();

    final initialJob = AudioDownloadJob(
      id: jobId,
      songId: song.id,
      songTitle: song.title,
      songArtist: song.artist ?? song.originalCreator,
      provider: 'youtube',
      canonicalUrl: song.url,
      status: 'RUNNING',
      progressPercent: 5,
      bitrateKbps: bitrateKbps,
      audioReady: false,
    );

    final tracked = _TrackedLocalJob(
      job: initialJob,
      song: song,
      cancelToken: token,
      bitrateKbps: bitrateKbps,
      onGuestStateChanged: onGuestStateChanged,
      onCompleted: onCompleted,
    );
    ownerJobs[jobId] = tracked;
    _emit(ownerKey, initialJob);

    if (onGuestStateChanged != null) {
      unawaited(onGuestStateChanged('RUNNING'));
    }

    unawaited(
      _execute(
        ownerKey: ownerKey,
        tracked: tracked,
        openLocalLibrary: openLocalLibrary,
      ),
    );
    return initialJob;
  }

  Future<void> _execute({
    required String ownerKey,
    required _TrackedLocalJob tracked,
    required Future<LocalLibrary> Function(String owner) openLocalLibrary,
  }) async {
    File? downloadedTemp;
    try {
      final local = await openLocalLibrary(ownerKey);
      final workDir =
          local.directory ??
          Directory(
            '${Directory.systemTemp.path}${Platform.pathSeparator}tmusic_ondevice',
          );

      final result = await downloader.downloadAudio(
        youtubeUrl: tracked.song.url,
        workDir: workDir,
        preferredBitrateKbps: tracked.bitrateKbps,
        cancelToken: tracked.cancelToken,
        onProgress: (pct) {
          if (tracked.cancelToken.isCanceled) return;
          tracked.job = _copyJob(tracked.job, progressPercent: pct);
          _emit(ownerKey, tracked.job);
        },
      );
      downloadedTemp = result.tempFile;

      if (tracked.cancelToken.isCanceled) {
        await _markCanceled(ownerKey, tracked);
        return;
      }

      if (local.directory != null && local.db != null) {
        await local.saveDownloadedSongFile(
          tracked.song,
          result.tempFile,
          extension: result.extension,
        );
      }
      if (await result.tempFile.exists()) {
        try {
          await result.tempFile.delete();
        } catch (_) {}
      }
      downloadedTemp = null;

      if (tracked.cancelToken.isCanceled) {
        await _markCanceled(ownerKey, tracked);
        return;
      }

      await tracked.onGuestStateChanged?.call('READY', audioReady: true);

      tracked.job = _copyJob(
        tracked.job,
        status: 'COMPLETE',
        progressPercent: 100,
        audioReady: true,
        outputSizeBytes: result.sizeBytes,
        errorCode: null,
        clearError: true,
      );
      _emit(ownerKey, tracked.job);
      tracked.onCompleted?.call();
    } catch (e) {
      if (downloadedTemp != null && await downloadedTemp.exists()) {
        try {
          await downloadedTemp.delete();
        } catch (_) {}
      }
      final failure = e is ApiFailure
          ? e
          : YoutubeExplodeOnDeviceDownloader.classifyError(e);
      if (tracked.cancelToken.isCanceled ||
          failure.code == 'AUDIO_DOWNLOAD_CANCELED') {
        await _markCanceled(ownerKey, tracked);
        return;
      }
      if (tracked.onGuestStateChanged != null) {
        try {
          await tracked.onGuestStateChanged?.call('FAILED');
        } catch (_) {}
      }
      tracked.job = _copyJob(
        tracked.job,
        status: 'FAILED',
        errorCode: failure.code,
        retryable: isRetryableDownloadError(failure.code),
        accessRestricted: const {
          'SOURCE_ACCESS_CHECK_REQUIRED',
          'SOURCE_AUTHENTICATION_REQUIRED',
          'SOURCE_ACCESS_FORBIDDEN',
        }.contains(failure.code.toUpperCase()),
      );
      _emit(ownerKey, tracked.job);
    }
  }

  Future<void> _markCanceled(String ownerKey, _TrackedLocalJob tracked) async {
    if (tracked.onGuestStateChanged != null) {
      try {
        await tracked.onGuestStateChanged?.call('NONE', audioReady: false);
      } catch (_) {}
    }
    tracked.job = _copyJob(
      tracked.job,
      status: 'CANCELED',
      clearError: true,
    );
    _emit(ownerKey, tracked.job);
  }

  Future<void> cancelOrDelete(String ownerKey, String jobId) async {
    final ownerJobs = _jobsByOwner[ownerKey];
    final tracked = ownerJobs?[jobId];
    if (tracked == null) return;
    if (tracked.job.isActive) {
      tracked.cancelToken.cancel();
      await _markCanceled(ownerKey, tracked);
    } else {
      ownerJobs?.remove(jobId);
    }
  }

  Future<AudioDownloadJob?> retry(
    String ownerKey,
    String jobId, {
    required Future<LocalLibrary> Function(String owner) openLocalLibrary,
  }) async {
    final tracked = _jobsByOwner[ownerKey]?[jobId];
    if (tracked == null) return null;
    _jobsByOwner[ownerKey]?.remove(jobId);
    return startYouTubeDownload(
      ownerKey: ownerKey,
      song: tracked.song,
      openLocalLibrary: openLocalLibrary,
      bitrateKbps: tracked.bitrateKbps,
      onGuestStateChanged: tracked.onGuestStateChanged,
      onCompleted: tracked.onCompleted,
    );
  }

  void _emit(String ownerKey, AudioDownloadJob job) {
    if (!_updates.isClosed) {
      _updates.add((ownerKey: ownerKey, job: job));
    }
  }

  static AudioDownloadJob _copyJob(
    AudioDownloadJob job, {
    String? status,
    int? progressPercent,
    bool? audioReady,
    String? errorCode,
    bool clearError = false,
    bool? retryable,
    bool? accessRestricted,
    int? outputSizeBytes,
  }) => AudioDownloadJob(
    id: job.id,
    songId: job.songId,
    songTitle: job.songTitle,
    songArtist: job.songArtist,
    provider: job.provider,
    canonicalUrl: job.canonicalUrl,
    status: status ?? job.status,
    progressPercent: progressPercent ?? job.progressPercent,
    audioReady: audioReady ?? job.audioReady,
    errorCode: clearError ? null : (errorCode ?? job.errorCode),
    errorCategory: clearError ? null : job.errorCategory,
    accessRestricted: accessRestricted ?? job.accessRestricted,
    retryable: retryable ?? job.retryable,
    bitrateKbps: job.bitrateKbps,
    audioStreamUrl: job.audioStreamUrl,
    outputSizeBytes: outputSizeBytes ?? job.outputSizeBytes,
  );

  void dispose() {
    for (final ownerMap in _jobsByOwner.values) {
      for (final tracked in ownerMap.values) {
        tracked.cancelToken.cancel();
      }
    }
    _updates.close();
  }
}
