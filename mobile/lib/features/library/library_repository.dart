import 'dart:math';
import 'dart:typed_data';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/session.dart';
import '../../core/storage/guest_library_store.dart';
import '../../core/storage/library_cache.dart';
import '../../shared/models/library_models.dart';
import '../playback/local_audio.dart';
import '../share/shared_links.dart';

final libraryRepositoryProvider = Provider((ref) {
  final userId = ref.watch(sessionProvider.select((s) => s.value?.userId));
  return LibraryRepository(
    api: ref.watch(authorizedApiProvider),
    publicApi: ref.watch(publicApiProvider),
    cache: ref.watch(libraryCacheProvider),
    guestStore: ref.watch(guestLibraryStoreProvider),
    offline: ref.read(offlineModeProvider.notifier),
    isGuest: userId == null,
    currentUserId: () => ref.read(sessionProvider).value?.userId,
  );
});

final libraryRevisionProvider = NotifierProvider<LibraryRevision, int>(
  LibraryRevision.new,
);

class LibraryRevision extends Notifier<int> {
  @override
  int build() => 0;
  void changed() => state++;
}

class LibraryRepository {
  LibraryRepository({
    required this.api,
    required this.publicApi,
    required this.cache,
    required this.guestStore,
    required this.offline,
    required this.isGuest,
    required this.currentUserId,
    this.openLocalLibrary = LocalLibrary.open,
  });

  final ApiClient api;
  final ApiClient publicApi;
  final LibraryCache cache;
  final GuestLibraryStore guestStore;
  final OfflineModeNotifier offline;
  final bool isGuest;
  final String? Function() currentUserId;
  final Future<LocalLibrary> Function(String owner) openLocalLibrary;

  static String _idempotencyKey() => List.generate(
    20,
    (_) => Random.secure().nextInt(256).toRadixString(16).padLeft(2, '0'),
  ).join();

  /// Claims local guest metadata and copies any MP3s to the account's device storage.
  Future<bool> syncGuestDataToCloud({required String userId}) async {
    try {
      if (isGuest || currentUserId() != userId) return false;
      final revision = guestStore.revision;
      final (guestAlbums, guestSongs) = await guestStore.exportAll();
      final guestAudio = await openLocalLibrary('guest');
      final importedTracks = (await guestAudio.list())
          .where((track) => !track.id.startsWith('song:'))
          .toList();
      if (guestAlbums.isEmpty && guestSongs.isEmpty && importedTracks.isEmpty) {
        return false;
      }
      final albumIdMap = <String, String>{};
      for (final album in guestAlbums.reversed) {
        if (currentUserId() != userId) return false;
        final created = await saveAlbum(
          name: album.name,
          description: album.description ?? '',
          cover: album.coverImageUrl?.startsWith('cover:') == true
              ? album.coverImageUrl
              : 'cover:green',
          favorite: album.isFavorite,
          sortOrder: album.sortOrder,
          idempotencyKey: album.id,
        );
        albumIdMap[album.id] = created.id;
      }

      LocalLibrary? userAudio;
      final copiedGuestSongIds = <String>[];
      for (final song in guestSongs.reversed) {
        if (currentUserId() != userId) return false;
        final mappedAlbums = song.albumIds
            .map((id) => albumIdMap[id])
            .whereType<String>()
            .toList();
        final saved = await saveLink(
          song.url,
          song.title,
          song.artist ?? '',
          mappedAlbums,
        );
        if (currentUserId() != userId) return false;
        if (song.favorite && !saved.favorite) await favorite(saved);
        if (song.audioReady) {
          final track = await guestAudio.getDownloadedSongTrack(song);
          if (track != null) {
            userAudio ??= await openLocalLibrary(userId);
            await userAudio.copyDownloadedSongMp3(saved, track);
            copiedGuestSongIds.add(song.id);
          }
        }
      }
      for (final track in importedTracks) {
        if (currentUserId() != userId) return false;
        userAudio ??= await openLocalLibrary(userId);
        await userAudio.copyImportedTrack(track);
      }
      if (currentUserId() != userId) return false;
      if (!await guestStore.clearAfterSync(revision)) return false;
      // All target copies are durable before removing the guest originals.
      for (final track in importedTracks) {
        await guestAudio.remove(track);
      }
      for (final songId in copiedGuestSongIds) {
        await guestAudio.removeSongTrack(songId);
      }
      return true;
    } catch (_) {
      // Retry with the same album creation keys on the next sign-in or app start.
      return false;
    }
  }

  Future<PageResult<Album>> albums({
    int page = 1,
    String query = '',
    String sort = 'recent',
    bool? favorite,
  }) async {
    if (isGuest) {
      return guestStore.listAlbums(
        query: query,
        sort: sort,
        favorite: favorite,
      );
    }
    final cacheKey = 'albums:$page:$sort:${favorite ?? "all"}:${query.trim()}';
    try {
      final raw = await api.request(
        'GET',
        '/albums',
        query: {
          'page': page,
          'pageSize': 30,
          'q': query,
          'sort': sort,
          'favorite': ?favorite,
        },
      );
      offline.setOffline(false);
      if (page == 1 && query.trim().isEmpty) {
        await cache.writeSnapshot(cacheKey, raw);
      }
      return PageResult.fromJson(raw, Album.fromJson);
    } on ApiFailure catch (e) {
      if (e.status == null && page == 1 && query.trim().isEmpty) {
        final snap = await cache.readSnapshot(cacheKey);
        if (snap != null) {
          offline.setOffline(true);
          return PageResult.fromJson(snap, Album.fromJson, fromCache: true);
        }
      }
      rethrow;
    }
  }

  Future<PageResult<Song>> songs({
    int page = 1,
    String query = '',
    String? provider,
    bool? favorite,
    bool? unorganized,
    String? albumId,
    String sort = 'recent',
  }) async {
    if (isGuest || albumId?.startsWith('guest-') == true) {
      return guestStore.listSongs(
        query: query,
        provider: provider,
        favorite: favorite,
        unorganized: unorganized,
        albumId: albumId,
        sort: sort,
      );
    }
    final cacheKey =
        'songs:${albumId ?? "lib"}:$page:$sort:${provider ?? "all"}:${favorite ?? "all"}:${unorganized ?? "all"}:${query.trim()}';
    try {
      final raw = await api.request(
        'GET',
        albumId == null ? '/library/songs' : '/albums/$albumId/songs',
        query: {
          'page': page,
          'pageSize': 30,
          'q': query,
          'provider': ?provider,
          'favorite': ?favorite,
          'unorganized': ?unorganized,
          'sort': sort,
        },
      );
      offline.setOffline(false);
      if (page == 1 && query.trim().isEmpty) {
        await cache.writeSnapshot(cacheKey, raw);
      }
      return PageResult.fromJson(raw, Song.fromJson);
    } on ApiFailure catch (e) {
      if (e.status == null && page == 1 && query.trim().isEmpty) {
        final snap = await cache.readSnapshot(cacheKey);
        if (snap != null) {
          offline.setOffline(true);
          return PageResult.fromJson(snap, Song.fromJson, fromCache: true);
        }
      }
      rethrow;
    }
  }

  Future<SearchResults> search(String query, {int page = 1}) async {
    if (isGuest) {
      final a = await guestStore.listAlbums(query: query);
      final s = await guestStore.listSongs(query: query);
      return SearchResults(albums: a, songs: s);
    }
    final raw = await api.request(
      'GET',
      '/search',
      query: {'q': query.trim(), 'page': page, 'pageSize': 30},
    );
    offline.setOffline(false);
    return SearchResults.fromJson(raw);
  }

  Future<Album> album(String id) async {
    if (isGuest || id.startsWith('guest-')) {
      return guestStore.getAlbum(id);
    }
    try {
      final raw = await api.request('GET', '/albums/$id');
      offline.setOffline(false);
      await cache.writeSnapshot('album:$id', raw);
      await cache.recordAlbumOpened(id);
      return Album.fromJson(raw);
    } on ApiFailure catch (e) {
      if (e.status == null) {
        final cached = await cache.findCachedAlbum(id);
        if (cached != null) {
          offline.setOffline(true);
          return Album.fromJson(cached);
        }
      }
      rethrow;
    }
  }

  Future<List<Album>> recentOpenedAlbums() async {
    if (isGuest) {
      final page = await guestStore.listAlbums();
      return page.items.take(6).toList();
    }
    final ids = await cache.recentAlbumIds();
    final all = await albums();
    if (ids.isEmpty) return all.items.take(6).toList();
    final map = {for (final a in all.items) a.id: a};
    final ordered = ids.map((id) => map[id]).whereType<Album>().toList();
    if (ordered.length < 6) {
      for (final a in all.items) {
        if (!ordered.any((x) => x.id == a.id)) ordered.add(a);
        if (ordered.length >= 6) break;
      }
    }
    return ordered;
  }

  Future<Song> song(String id) async {
    if (isGuest || id.startsWith('guest-')) {
      return guestStore.getSong(id);
    }
    try {
      final raw = await api.request('GET', '/library/songs/$id');
      offline.setOffline(false);
      await cache.writeSnapshot('song:$id', raw);
      return Song.fromJson(raw);
    } on ApiFailure catch (e) {
      if (e.status == null) {
        final cached = await cache.findCachedSong(id);
        if (cached != null) {
          offline.setOffline(true);
          return Song.fromJson(cached);
        }
      }
      rethrow;
    }
  }

  Future<Album> saveAlbum({
    Album? existing,
    required String name,
    required String description,
    String? cover,
    bool? favorite,
    int? sortOrder,
    String? idempotencyKey,
  }) async {
    if (isGuest || (existing?.isGuestLocal == true)) {
      return guestStore.saveAlbum(
        existing: existing,
        name: name,
        description: description,
        cover: cover,
        favorite: favorite,
        sortOrder: sortOrder,
      );
    }
    return Album.fromJson(
      await api.request(
        existing == null ? 'POST' : 'PATCH',
        existing == null ? '/albums' : '/albums/${existing.id}',
        version: existing?.version,
        headers: existing == null
            ? {'Idempotency-Key': idempotencyKey ?? _idempotencyKey()}
            : null,
        data: {
          'name': name,
          'description': description,
          'coverImageUrl': cover,
          'isFavorite': favorite ?? existing?.isFavorite ?? false,
          'sortOrder': sortOrder ?? existing?.sortOrder ?? 0,
        },
      ),
    );
  }

  Future<Album> uploadAlbumCover(
    Album album, {
    required String fileName,
    required Uint8List bytes,
  }) async {
    if (isGuest || album.isGuestLocal) return album;
    return Album.fromJson(
      await api.uploadMultipart(
        '/albums/${album.id}/cover',
        fileName: fileName,
        bytes: bytes,
        version: album.version,
      ),
    );
  }

  Future<Album> removeAlbumCover(Album album) async {
    if (isGuest || album.isGuestLocal) {
      return guestStore.saveAlbum(
        existing: album,
        name: album.name,
        description: album.description ?? '',
        cover: null,
      );
    }
    return Album.fromJson(
      await api.request(
        'DELETE',
        '/albums/${album.id}/cover',
        version: album.version,
      ),
    );
  }

  Future<void> reorderAlbums(List<String> ids) {
    if (isGuest) return guestStore.reorderAlbums(ids);
    return api.request('PATCH', '/albums/order', data: {'albumIds': ids});
  }

  Future<void> deleteAlbum(Album a) {
    if (isGuest || a.isGuestLocal) return guestStore.deleteAlbum(a.id);
    return api.request('DELETE', '/albums/${a.id}', version: a.version);
  }

  Future<LinkPreview> resolve(String url) async {
    if (isGuest) {
      var dup = await guestStore.findByCanonicalUrl(url.trim());
      try {
        final raw = await publicApi.request(
          'POST',
          '/guest/songs/resolve',
          data: {'url': url.trim()},
        );
        final parsed = LinkPreview.fromJson(raw);
        dup ??= await guestStore.findByCanonicalUrl(parsed.canonicalUrl);
        if (dup != null) {
          return LinkPreview(
            provider: parsed.provider,
            providerContentId: parsed.providerContentId,
            canonicalUrl: parsed.canonicalUrl,
            capability: parsed.capability,
            title: parsed.title,
            creator: parsed.creator,
            thumbnail: parsed.thumbnail,
            durationSeconds: parsed.durationSeconds,
            existingId: dup.id,
            existingSong: dup,
            notice: parsed.notice,
          );
        }
        return parsed;
      } on ApiFailure catch (e) {
        if (e.status != null) rethrow;
        // Offline Guest fallback when backend is unreachable
        final prov = SharedLinks.detectProvider(url);
        return LinkPreview(
          provider: prov,
          providerContentId: url.trim(),
          canonicalUrl: url.trim(),
          capability: 'EXTERNAL_OPEN_ONLY',
          existingId: dup?.id,
          existingSong: dup,
        );
      }
    }
    return LinkPreview.fromJson(
      await api.request('POST', '/songs/resolve', data: {'url': url}),
    );
  }

  Future<Song> saveLink(
    String url,
    String title,
    String artist,
    List<String> albums, {
    bool downloadMp3 = false,
    bool rightsConfirmed = false,
  }) async {
    if (isGuest) {
      LinkPreview? preview;
      try {
        preview = await resolve(url);
      } catch (_) {}
      final prov = preview?.provider ?? SharedLinks.detectProvider(url);
      var saved = await guestStore.saveSong(
        url: preview?.canonicalUrl ?? url.trim(),
        provider: prov,
        providerContentId: preview?.providerContentId ?? url.trim(),
        title: title,
        artist: artist,
        originalTitle: preview?.title,
        originalCreator: preview?.creator,
        thumbnail: preview?.thumbnail,
        durationSeconds: preview?.durationSeconds,
        albumIds: albums,
      );
      if (downloadMp3 && rightsConfirmed) {
        try {
          await startSongDownload(saved.id);
        } catch (_) {
          // The source stays saved; its failed job is visible and retryable.
        }
        saved = await guestStore.getSong(saved.id);
      }
      return saved;
    }
    return Song.fromJson(
      await api.request(
        'POST',
        '/library/songs',
        data: {
          'url': url,
          'customTitle': title,
          'customArtist': artist,
          'albumIds': albums,
          'downloadMp3': downloadMp3,
          'rightsConfirmed': rightsConfirmed,
        },
      ),
    );
  }

  Future<AudioDownloadJob> startSongDownload(
    String songId, {
    bool force = false,
  }) async {
    if (isGuest || songId.startsWith('guest-')) {
      final s = await guestStore.getSong(songId);
      await guestStore.updateSong(s.id, audioStatus: 'RUNNING');
      try {
        final bytes = await publicApi.postBytes(
          '/guest/songs/extract-mp3',
          data: {'url': s.url, 'rightsConfirmed': true},
        );
        final local = await openLocalLibrary('guest');
        await local.saveDownloadedSongMp3(s, bytes);
        return _guestDownloadJob(
          await guestStore.updateSong(s.id, audioReady: true),
        );
      } catch (_) {
        await guestStore.updateSong(s.id, audioStatus: 'FAILED');
        rethrow;
      }
    }
    return AudioDownloadJob.fromJson(
      await api.request(
        'POST',
        '/library/songs/$songId/download',
        data: {'rightsConfirmed': true, 'forceRedownload': force},
      ),
    );
  }

  Future<void> removeSongAudio(String songId) async {
    if (isGuest || songId.startsWith('guest-')) {
      await guestStore.getSong(songId);
      final local = await openLocalLibrary('guest');
      await local.removeSongTrack(songId);
      await guestStore.updateSong(songId, audioReady: false);
      return;
    }
    await api.request('DELETE', '/library/songs/$songId/audio');
  }

  Future<PageResult<AudioDownloadJob>> downloadJobs({int page = 1}) async {
    if (isGuest) {
      final s = await guestStore.listSongs();
      final ready = s.items
          .where((x) => x.audioReady || x.audioStatus != 'NONE')
          .map(_guestDownloadJob)
          .toList();
      return PageResult(ready, ready.length, 1, max(50, ready.length));
    }
    return PageResult.fromJson(
      await api.request(
        'GET',
        '/downloads',
        query: {'page': page, 'pageSize': 30},
      ),
      AudioDownloadJob.fromJson,
    );
  }

  AudioDownloadJob _guestDownloadJob(Song song) => AudioDownloadJob(
    id: 'guest-job-${song.id}',
    songId: song.id,
    songTitle: song.title,
    songArtist: song.artist ?? song.originalCreator,
    provider: song.provider,
    canonicalUrl: song.url,
    status: song.audioStatus == 'FAILED'
        ? 'FAILED'
        : song.audioReady
        ? 'COMPLETE'
        : song.audioStatus,
    progressPercent: song.audioReady ? 100 : 0,
    audioReady: song.audioReady,
  );

  String _guestJobSongId(String jobId) {
    if (!jobId.startsWith('guest-job-guest-')) {
      throw const ApiFailure('not_found', status: 404);
    }
    return jobId.substring('guest-job-'.length);
  }

  Future<AudioDownloadJob> downloadJob(String jobId) async {
    if (isGuest || jobId.startsWith('guest-job-')) {
      return _guestDownloadJob(
        await guestStore.getSong(_guestJobSongId(jobId)),
      );
    }
    return AudioDownloadJob.fromJson(
      await api.request('GET', '/downloads/$jobId'),
    );
  }

  Future<AudioDownloadJob> retryDownloadJob(String jobId) async {
    if (isGuest || jobId.startsWith('guest-job-')) {
      return startSongDownload(_guestJobSongId(jobId));
    }
    return AudioDownloadJob.fromJson(
      await api.request('POST', '/downloads/$jobId/retry'),
    );
  }

  Future<void> cancelOrDeleteDownloadJob(String jobId) async {
    if (isGuest || jobId.startsWith('guest-job-')) {
      await removeSongAudio(_guestJobSongId(jobId));
      return;
    }
    await api.request('DELETE', '/downloads/$jobId');
  }

  Future<Uint8List> fetchSongAudioBytes(String songId) {
    if (isGuest || songId.startsWith('guest-')) {
      throw const ApiFailure('not_found', status: 404);
    }
    return api.fetchBytes('/api/v1/library/songs/$songId/audio');
  }

  Future<Song> editSong(Song s, String title, String artist) async {
    if (isGuest || s.isGuestLocal) {
      return guestStore.updateSong(s.id, title: title, artist: artist);
    }
    return Song.fromJson(
      await api.request(
        'PATCH',
        '/library/songs/${s.id}',
        version: s.version,
        data: {'customTitle': title, 'customArtist': artist},
      ),
    );
  }

  Future<Song> favorite(Song s) async {
    if (isGuest || s.isGuestLocal) {
      return guestStore.updateSong(s.id, favorite: !s.favorite);
    }
    return Song.fromJson(
      await api.request(
        'PATCH',
        '/library/songs/${s.id}/favorite',
        version: s.version,
        data: {'isFavorite': !s.favorite},
      ),
    );
  }

  Future<void> deleteSong(Song s) {
    if (isGuest || s.isGuestLocal) return guestStore.deleteSong(s.id);
    return api.request('DELETE', '/library/songs/${s.id}', version: s.version);
  }

  Future<void> addToAlbum(Album a, Song s) async {
    if (isGuest || a.isGuestLocal || s.isGuestLocal) {
      await guestStore.updateSong(
        s.id,
        albumIds: {...s.albumIds, a.id}.toList(),
      );
      return;
    }
    await api.request(
      'POST',
      '/albums/${a.id}/songs',
      version: a.version,
      data: {'userLibrarySongId': s.id},
    );
  }

  Future<void> removeFromAlbum(Album a, Song s) async {
    if (isGuest || a.isGuestLocal || s.isGuestLocal) {
      await guestStore.updateSong(
        s.id,
        albumIds: s.albumIds.where((id) => id != a.id).toList(),
      );
      return;
    }
    await api.request(
      'DELETE',
      '/albums/${a.id}/songs/${s.id}',
      version: a.version,
    );
  }

  Future<void> order(Album a, List<String> ids) async {
    if (isGuest || a.isGuestLocal) {
      await guestStore.orderAlbumSongs(a.id, ids);
      return;
    }
    await api.request(
      'PATCH',
      '/albums/${a.id}/songs/order',
      version: a.version,
      data: {'songIds': ids},
    );
  }

  // --- Album Sharing Between TMusic Users ---

  Future<AlbumShareInfo?> getAlbumShare(String albumId) async {
    if (isGuest || albumId.startsWith('guest-')) return null;
    try {
      final raw = await api.request('GET', '/albums/$albumId/share');
      return AlbumShareInfo.fromJson(raw);
    } on ApiFailure catch (e) {
      if (e.status == 404) return null;
      rethrow;
    }
  }

  Future<AlbumShareInfo> createAlbumShare(String albumId) async {
    final raw = await api.request('POST', '/albums/$albumId/share');
    return AlbumShareInfo.fromJson(raw);
  }

  Future<void> revokeAlbumShare(String albumId) =>
      api.request('DELETE', '/albums/$albumId/share');

  Future<SharedAlbumPreview> previewSharedAlbum(String code) async {
    final clean = code.trim().toUpperCase();
    SharedAlbumPreview? first;
    final songs = <SharedSongItem>[];
    for (var page = 1; page <= 25; page++) {
      final raw = await publicApi.request(
        'GET',
        '/shared-albums/$clean',
        query: {'page': page, 'pageSize': 200},
      );
      final result = SharedAlbumPreview.fromJson(raw);
      first ??= result;
      if (result.songCount > 5000 ||
          songs.length + result.songs.length > 5000) {
        throw const ApiFailure('SHARED_ALBUM_TOO_LARGE', status: 409);
      }
      if (result.songCount != first.songCount) {
        throw const ApiFailure('SHARED_ALBUM_CHANGED', status: 409);
      }
      songs.addAll(result.songs);
      if (raw['hasMore'] != true) {
        if (raw['hasMore'] == false && songs.length != first.songCount) {
          throw const ApiFailure('SHARED_ALBUM_CHANGED', status: 409);
        }
        return SharedAlbumPreview(
          shareCode: first.shareCode,
          albumName: first.albumName,
          sharedBy: first.sharedBy,
          songCount: first.songCount,
          songs: songs,
          description: first.description,
          coverImageUrl: first.coverImageUrl,
        );
      }
      if (result.songs.isEmpty) {
        throw const ApiFailure('SHARED_ALBUM_CHANGED', status: 409);
      }
    }
    throw const ApiFailure('SHARED_ALBUM_TOO_LARGE', status: 409);
  }

  Future<Album> importSharedAlbum(String code) async {
    final clean = code.trim().toUpperCase();
    if (isGuest) {
      final preview = await previewSharedAlbum(clean);
      final created = await guestStore.saveAlbum(
        name: preview.albumName,
        description: preview.description ?? 'Chia sẻ từ ${preview.sharedBy}',
        cover: preview.coverImageUrl ?? 'cover:green',
      );
      final songIds = <String>[];
      for (final item in preview.songs) {
        final song = await guestStore.saveSong(
          url: item.canonicalUrl,
          provider: item.provider,
          providerContentId: item.providerContentId.isEmpty
              ? item.canonicalUrl
              : item.providerContentId,
          title: item.title,
          artist: item.artist,
          originalTitle: item.originalTitle,
          originalCreator: item.originalCreator,
          thumbnail: item.thumbnailUrl,
          durationSeconds: item.durationSeconds,
          albumIds: [created.id],
        );
        songIds.add(song.id);
      }
      await guestStore.orderAlbumSongs(created.id, songIds);
      return guestStore.getAlbum(created.id);
    }
    final raw = await api.request('POST', '/shared-albums/$clean/import');
    return Album.fromJson(raw);
  }
}
