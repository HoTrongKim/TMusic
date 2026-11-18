String providerDisplayLabel(String provider) => switch (provider) {
  'soundcloud' => 'SoundCloud',
  'tiktok' => 'TikTok',
  _ => 'YouTube',
};

class PageResult<T> {
  const PageResult(
    this.items,
    this.total,
    this.page,
    this.pageSize, {
    this.fromCache = false,
  });

  final List<T> items;
  final int total, page, pageSize;
  final bool fromCache;

  bool get hasMore => page * pageSize < total;

  factory PageResult.fromJson(
    Map<String, dynamic> j,
    T Function(Map<String, dynamic>) parse, {
    bool fromCache = false,
  }) => PageResult(
    (j['items'] as List? ?? [])
        .map((e) => parse(Map<String, dynamic>.from(e as Map)))
        .toList(),
    (j['total'] as num?)?.toInt() ?? 0,
    (j['page'] as num?)?.toInt() ?? 1,
    (j['pageSize'] as num?)?.toInt() ?? 30,
    fromCache: fromCache,
  );
}

class Album {
  const Album({
    required this.id,
    required this.name,
    required this.version,
    this.description,
    this.coverImageUrl,
    this.isFavorite = false,
    this.songCount = 0,
    this.sortOrder = 0,
  });

  final String id, name, version;
  final String? description, coverImageUrl;
  final bool isFavorite;
  final int songCount, sortOrder;

  bool get isGuestLocal => id.startsWith('guest-');

  factory Album.fromJson(Map<String, dynamic> j) => Album(
    id: j['id'] as String,
    name: j['name'] as String,
    version: j['version'] as String? ?? '',
    description: j['description'] as String?,
    coverImageUrl: j['coverImageUrl'] as String?,
    isFavorite: j['isFavorite'] == true,
    songCount: (j['songCount'] as num?)?.toInt() ?? 0,
    sortOrder: (j['sortOrder'] as num?)?.toInt() ?? 0,
  );

  Album copyWith({
    String? name,
    String? description,
    String? coverImageUrl,
    bool? isFavorite,
    int? songCount,
    int? sortOrder,
  }) => Album(
    id: id,
    name: name ?? this.name,
    version: version,
    description: description ?? this.description,
    coverImageUrl: coverImageUrl ?? this.coverImageUrl,
    isFavorite: isFavorite ?? this.isFavorite,
    songCount: songCount ?? this.songCount,
    sortOrder: sortOrder ?? this.sortOrder,
  );

  Map<String, dynamic> toJson() => {
    'id': id,
    'name': name,
    'version': version,
    'description': description,
    'coverImageUrl': coverImageUrl,
    'isFavorite': isFavorite,
    'songCount': songCount,
    'sortOrder': sortOrder,
  };
}

class Song {
  Song({
    required this.id,
    required this.version,
    required this.title,
    required this.customTitle,
    required this.provider,
    required this.providerContentId,
    required this.url,
    required this.originalUrl,
    required this.capability,
    this.artist,
    this.originalTitle,
    this.originalCreator,
    this.thumbnail,
    this.durationSeconds,
    this.audioReady = false,
    this.audioStatus = 'NONE',
    this.audioStreamUrl,
    this.audioSizeBytes,
    this.downloadJobId,
    this.favorite = false,
    this.albumIds = const [],
  });

  factory Song.fromJson(Map<String, dynamic> j) {
    final rawCustom = (j['customTitle'] as String? ?? '').trim();
    final origTitle = j['originalTitle'] as String?;
    final provider = j['provider'] as String? ?? 'youtube';
    final canonical = j['canonicalUrl'] as String? ?? '';
    final fallbackLabel = switch (provider) {
      'soundcloud' => 'Bản nhạc SoundCloud',
      'tiktok' => 'Video TikTok',
      _ => 'Video YouTube',
    };
    return Song(
      id: j['id'] as String,
      version: j['version'] as String? ?? '',
      customTitle: rawCustom,
      title: rawCustom.isNotEmpty
          ? rawCustom
          : (origTitle?.trim().isNotEmpty == true ? origTitle! : fallbackLabel),
      artist: j['customArtist'] as String?,
      provider: provider,
      providerContentId: j['providerContentId'] as String? ?? '',
      url: canonical,
      originalUrl: j['originalUrl'] as String? ?? canonical,
      originalTitle: origTitle,
      originalCreator: j['originalCreator'] as String?,
      thumbnail: j['thumbnailUrl'] as String?,
      durationSeconds: (j['durationSeconds'] as num?)?.toInt(),
      capability: j['playbackCapability'] as String? ?? 'UNKNOWN',
      audioReady: j['audioReady'] == true,
      audioStatus: j['audioStatus'] as String? ?? 'NONE',
      audioStreamUrl: j['audioStreamUrl'] as String?,
      audioSizeBytes: (j['audioSizeBytes'] as num?)?.toInt(),
      downloadJobId: j['downloadJobId'] as String?,
      favorite: j['isFavorite'] == true,
      albumIds: (j['albumIds'] as List? ?? []).map((e) => '$e').toList(),
    );
  }

  final String id,
      version,
      title,
      customTitle,
      provider,
      providerContentId,
      url,
      originalUrl,
      capability,
      audioStatus;
  final String? artist,
      originalTitle,
      originalCreator,
      thumbnail,
      audioStreamUrl,
      downloadJobId;
  final int? durationSeconds, audioSizeBytes;
  final bool favorite, audioReady;
  final List<String> albumIds;

  bool get isGuestLocal => id.startsWith('guest-');
  String get providerLabel => providerDisplayLabel(provider);

  /// Explicit video/audio source attribution ("Nguồn: YouTube / TikTok / SoundCloud • Creator")
  String get sourceAttribution {
    final creator = (originalCreator ?? artist)?.trim();
    if (creator != null && creator.isNotEmpty) {
      return 'Nguồn: $providerLabel • $creator';
    }
    return 'Nguồn: $providerLabel';
  }

  Song copyWith({
    String? title,
    String? customTitle,
    String? artist,
    bool? favorite,
    bool? audioReady,
    String? audioStatus,
    List<String>? albumIds,
  }) => Song(
    id: id,
    version: version,
    title: title ?? this.title,
    customTitle: customTitle ?? this.customTitle,
    provider: provider,
    providerContentId: providerContentId,
    url: url,
    originalUrl: originalUrl,
    capability: capability,
    artist: artist ?? this.artist,
    originalTitle: originalTitle,
    originalCreator: originalCreator,
    thumbnail: thumbnail,
    durationSeconds: durationSeconds,
    audioReady: audioReady ?? this.audioReady,
    audioStatus: audioStatus ?? this.audioStatus,
    audioStreamUrl: audioStreamUrl,
    audioSizeBytes: audioSizeBytes,
    downloadJobId: downloadJobId,
    favorite: favorite ?? this.favorite,
    albumIds: albumIds ?? this.albumIds,
  );

  Map<String, dynamic> toJson() => {
    'id': id,
    'version': version,
    'customTitle': customTitle,
    'customArtist': artist,
    'provider': provider,
    'providerContentId': providerContentId,
    'canonicalUrl': url,
    'originalUrl': originalUrl,
    'originalTitle': originalTitle,
    'originalCreator': originalCreator,
    'thumbnailUrl': thumbnail,
    'durationSeconds': durationSeconds,
    'playbackCapability': capability,
    'audioReady': audioReady,
    'audioStatus': audioStatus,
    'audioStreamUrl': audioStreamUrl,
    'audioSizeBytes': audioSizeBytes,
    'downloadJobId': downloadJobId,
    'isFavorite': favorite,
    'albumIds': albumIds,
  };
}

class AudioDownloadJob {
  const AudioDownloadJob({
    required this.id,
    required this.songId,
    required this.provider,
    required this.canonicalUrl,
    required this.status,
    required this.progressPercent,
    required this.audioReady,
    this.songTitle,
    this.songArtist,
    this.errorCode,
    this.audioStreamUrl,
    this.outputSizeBytes,
  });

  final String id, songId, provider, canonicalUrl, status;
  final int progressPercent;
  final bool audioReady;
  final String? songTitle, songArtist, errorCode, audioStreamUrl;
  final int? outputSizeBytes;

  bool get isActive => status == 'QUEUED' || status == 'RUNNING';
  String get providerLabel => providerDisplayLabel(provider);

  factory AudioDownloadJob.fromJson(Map<String, dynamic> j) => AudioDownloadJob(
    id: j['id'] as String,
    songId: j['songId'] as String,
    songTitle: j['songTitle'] as String?,
    songArtist: j['songArtist'] as String?,
    provider: j['provider'] as String? ?? 'youtube',
    canonicalUrl: j['canonicalUrl'] as String? ?? '',
    status: j['status'] as String? ?? 'QUEUED',
    progressPercent: (j['progressPercent'] as num?)?.toInt() ?? 0,
    errorCode: j['errorCode'] as String?,
    audioReady: j['audioReady'] == true,
    audioStreamUrl: j['audioStreamUrl'] as String?,
    outputSizeBytes: (j['outputSizeBytes'] as num?)?.toInt(),
  );
}

class LinkPreview {
  LinkPreview({
    required this.provider,
    required this.providerContentId,
    required this.canonicalUrl,
    required this.capability,
    this.title,
    this.creator,
    this.thumbnail,
    this.durationSeconds,
    this.existingId,
    this.existingSong,
    this.notice,
  });

  LinkPreview.fromJson(Map<String, dynamic> j)
    : provider = j['provider'] as String,
      providerContentId = j['providerContentId'] as String? ?? '',
      canonicalUrl = j['canonicalUrl'] as String,
      title = j['originalTitle'] as String?,
      creator = j['originalCreator'] as String?,
      thumbnail = j['thumbnailUrl'] as String?,
      durationSeconds = (j['durationSeconds'] as num?)?.toInt(),
      existingId = j['existingLibrarySongId'] as String?,
      existingSong = j['existingSong'] is Map
          ? Song.fromJson(Map<String, dynamic>.from(j['existingSong'] as Map))
          : null,
      notice = j['notice'] as String?,
      capability = j['playbackCapability'] as String? ?? 'UNKNOWN';

  final String provider, providerContentId, canonicalUrl, capability;
  final String? title, creator, thumbnail, existingId, notice;
  final int? durationSeconds;
  final Song? existingSong;

  String get providerLabel => providerDisplayLabel(provider);
  String get sourceAttribution => creator?.trim().isNotEmpty == true
      ? 'Nguồn: $providerLabel • ${creator!.trim()}'
      : 'Nguồn: $providerLabel';
}

class SearchResults {
  const SearchResults({required this.albums, required this.songs});
  final PageResult<Album> albums;
  final PageResult<Song> songs;

  factory SearchResults.fromJson(Map<String, dynamic> j) => SearchResults(
    albums: PageResult.fromJson(
      Map<String, dynamic>.from(j['albums'] as Map? ?? const {}),
      Album.fromJson,
    ),
    songs: PageResult.fromJson(
      Map<String, dynamic>.from(j['songs'] as Map? ?? const {}),
      Song.fromJson,
    ),
  );
}

class AlbumShareInfo {
  const AlbumShareInfo({required this.shareCode, required this.albumId});
  final String shareCode;
  final String albumId;

  factory AlbumShareInfo.fromJson(Map<String, dynamic> j) => AlbumShareInfo(
    shareCode: j['shareCode'] as String,
    albumId: '${j['albumId'] ?? ''}',
  );
}

class SharedSongItem {
  const SharedSongItem({
    required this.title,
    required this.provider,
    required this.canonicalUrl,
    this.providerContentId = '',
    this.artist,
    this.originalTitle,
    this.originalCreator,
    this.thumbnailUrl,
    this.durationSeconds,
  });

  final String title, provider, providerContentId, canonicalUrl;
  final String? artist, originalTitle, originalCreator, thumbnailUrl;
  final int? durationSeconds;

  String get providerLabel => providerDisplayLabel(provider);
  String get sourceAttribution {
    final creator = (originalCreator ?? artist)?.trim();
    return creator != null && creator.isNotEmpty
        ? 'Nguồn: $providerLabel • $creator'
        : 'Nguồn: $providerLabel';
  }

  factory SharedSongItem.fromJson(Map<String, dynamic> j) => SharedSongItem(
    title: (j['customTitle'] as String? ?? '').trim().isNotEmpty
        ? (j['customTitle'] as String).trim()
        : (j['originalTitle'] as String? ?? 'Bài hát'),
    artist: j['customArtist'] as String?,
    provider: j['provider'] as String? ?? 'youtube',
    canonicalUrl: j['canonicalUrl'] as String? ?? '',
    providerContentId: j['providerContentId'] as String? ?? '',
    originalTitle: j['originalTitle'] as String?,
    originalCreator: j['originalCreator'] as String?,
    thumbnailUrl: j['thumbnailUrl'] as String?,
    durationSeconds: (j['durationSeconds'] as num?)?.toInt(),
  );
}

class SharedAlbumPreview {
  const SharedAlbumPreview({
    required this.shareCode,
    required this.albumName,
    required this.sharedBy,
    required this.songCount,
    required this.songs,
    this.description,
    this.coverImageUrl,
  });

  final String shareCode, albumName, sharedBy;
  final int songCount;
  final String? description, coverImageUrl;
  final List<SharedSongItem> songs;

  factory SharedAlbumPreview.fromJson(Map<String, dynamic> j) =>
      SharedAlbumPreview(
        shareCode: j['shareCode'] as String? ?? '',
        albumName: j['albumName'] as String? ?? '',
        description: j['description'] as String?,
        coverImageUrl: j['coverImageUrl'] as String?,
        sharedBy: j['sharedBy'] as String? ?? 'Người dùng TMusic',
        songCount: (j['songCount'] as num?)?.toInt() ?? 0,
        songs: (j['songs'] as List? ?? const [])
            .map(
              (e) =>
                  SharedSongItem.fromJson(Map<String, dynamic>.from(e as Map)),
            )
            .toList(),
      );
}
