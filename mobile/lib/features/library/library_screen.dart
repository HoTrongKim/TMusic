import 'dart:async';

import 'package:audio_service/audio_service.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../app/theme/tmusic_theme.dart';
import '../../core/auth/session.dart';
import '../../core/localization/app_copy.dart';
import '../../core/widgets/common.dart';
import '../../shared/models/library_models.dart';
import '../albums/album_share_sheets.dart';
import '../playback/local_audio.dart';
import 'library_repository.dart';

/// Cross & MusicRoom-inspired Compact Track Row with Live Equalizer Highlight.
class SongTile extends ConsumerWidget {
  const SongTile(
    this.song, {
    super.key,
    this.trailing,
    this.trackNumber,
    this.onPlayTap,
    this.isDownloaded = false,
  });

  final Song song;
  final Widget? trailing;
  final int? trackNumber;
  final VoidCallback? onPlayTap;
  final bool isDownloaded;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final scheme = Theme.of(context).colorScheme;
    final duration = formatDuration(song.durationSeconds);
    final subtitle = [song.sourceAttribution, ?duration].join(' · ');

    final audioAsync = ref.watch(localAudioProvider);
    final player = audioAsync.hasValue ? audioAsync.value : null;

    Widget buildRow(bool isCurrent) {
      return Material(
        color: isCurrent
            ? scheme.primary.withValues(alpha: 0.11)
            : Colors.transparent,
        borderRadius: BorderRadius.circular(12),
        child: InkWell(
          borderRadius: BorderRadius.circular(12),
          onTap: onPlayTap ?? () => context.push('/songs/${song.id}'),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 7),
            child: Row(
              children: [
                if (trackNumber != null)
                  SizedBox(
                    width: 28,
                    child: isCurrent
                        ? Icon(
                            Icons.graphic_eq_rounded,
                            size: 18,
                            color: scheme.primary,
                          )
                        : Text(
                            '$trackNumber'.padLeft(2, '0'),
                            style: Theme.of(context).textTheme.bodySmall
                                ?.copyWith(fontWeight: FontWeight.w700),
                          ),
                  ),
                Stack(
                  alignment: Alignment.center,
                  children: [
                    Cover(url: song.thumbnail, size: 50, radius: 6),
                    if (isCurrent && trackNumber == null)
                      Container(
                        width: 50,
                        height: 50,
                        decoration: BoxDecoration(
                          color: Colors.black.withValues(alpha: 0.45),
                          borderRadius: BorderRadius.circular(6),
                        ),
                        child: const Icon(
                          Icons.graphic_eq_rounded,
                          color: Colors.white,
                          size: 22,
                        ),
                      ),
                  ],
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        song.title,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: Theme.of(context).textTheme.titleMedium
                            ?.copyWith(
                              fontSize: 14.5,
                              fontWeight: isCurrent
                                  ? FontWeight.w800
                                  : FontWeight.w600,
                              color: isCurrent
                                  ? TMusicTheme.spotifyGreen
                                  : null,
                            ),
                      ),
                      const SizedBox(height: 2),
                      Text(
                        subtitle,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: 4),
                if (song.audioReady || isDownloaded)
                  Padding(
                    padding: const EdgeInsets.only(right: 4),
                    child: Icon(
                      Icons.arrow_circle_down_rounded,
                      semanticLabel: context.tr('downloadedFilter'),
                      color: TMusicTheme.spotifyGreen,
                      size: 18,
                    ),
                  ),
                trailing ??
                    Icon(
                      song.favorite
                          ? Icons.favorite_rounded
                          : Icons.chevron_right_rounded,
                      size: 20,
                      color: song.favorite
                          ? const Color(0xFFE05263)
                          : scheme.onSurfaceVariant,
                    ),
              ],
            ),
          ),
        ),
      );
    }

    if (player == null) return buildRow(false);
    return StreamBuilder<MediaItem?>(
      initialData: player.mediaItem.value,
      stream: player.mediaItem,
      builder: (context, snap) => buildRow(snap.data?.id == 'song:${song.id}'),
    );
  }
}

class AlbumTile extends StatelessWidget {
  const AlbumTile(this.album, {super.key, this.trailing});
  final Album album;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Material(
      color: Colors.transparent,
      borderRadius: BorderRadius.circular(12),
      child: InkWell(
        borderRadius: BorderRadius.circular(12),
        onTap: () => context.push('/albums/${album.id}'),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 7),
          child: Row(
            children: [
              Cover(url: album.coverImageUrl, size: 54, radius: 10),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      album.name,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: Theme.of(context).textTheme.titleMedium
                          ?.copyWith(fontWeight: FontWeight.w700),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      '${album.songCount} ${context.tr('songs').toLowerCase()}',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ],
                ),
              ),
              trailing ??
                  Icon(
                    album.isFavorite
                        ? Icons.favorite_rounded
                        : Icons.chevron_right_rounded,
                    size: 20,
                    color: album.isFavorite
                        ? const Color(0xFFE05263)
                        : scheme.onSurfaceVariant,
                  ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Matchy & Apple Music-inspired Borderless Vinyl Cover Card.
class AlbumGridCard extends StatelessWidget {
  const AlbumGridCard(this.album, {super.key});
  final Album album;

  @override
  Widget build(BuildContext context) => InkWell(
    borderRadius: BorderRadius.circular(14),
    onTap: () => context.push('/albums/${album.id}'),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        AspectRatio(
          aspectRatio: 1,
          child: Cover(url: album.coverImageUrl, size: 170, radius: 14),
        ),
        const SizedBox(height: 8),
        Text(
          album.name,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: Theme.of(context).textTheme.titleMedium
              ?.copyWith(fontWeight: FontWeight.w700),
        ),
        const SizedBox(height: 2),
        Text(
          '${album.songCount} ${context.tr('songs').toLowerCase()}',
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: Theme.of(context).textTheme.bodySmall,
        ),
      ],
    ),
  );
}

/// Music Library Screen — Segmented Pill Tabs, Pill Search Bar, Horizontal Filter Chips & Vinyl Grid.
class LibraryScreen extends ConsumerStatefulWidget {
  const LibraryScreen({super.key});
  @override
  ConsumerState<LibraryScreen> createState() => _LibraryScreenState();
}

class _LibraryScreenState extends ConsumerState<LibraryScreen> {
  final _query = TextEditingController();
  Timer? _debounce;
  String _section = 'albums';
  bool _showSearch = false;
  bool _grid = true;
  bool _reordering = false;
  bool _busy = true;
  Object? _error;
  String? _provider;
  String _sort = 'recent';
  int _page = 1;
  bool _more = false;
  List<Album> _albumItems = [];
  List<Song> _songItems = [];
  Set<String> _downloadedTrackIds = {};
  int _request = 0;

  bool get _albums => _section == 'albums' && _provider == null;
  bool get _unorganizedOnly => _section == 'unclassified';
  bool get _downloadedOnly => _section == 'downloaded';

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _request++;
    _debounce?.cancel();
    _query.dispose();
    super.dispose();
  }

  Future<void> _load({bool append = false}) async {
    final generation = ++_request;
    var page = append ? _page + 1 : 1;
    final query = _query.text.trim();
    final sort = _sort;
    final provider = _provider;
    final unorganized = _unorganizedOnly;
    final downloaded = _downloadedOnly;
    setState(() {
      _busy = true;
      _error = null;
      if (!append) {
        _albumItems = [];
        _songItems = [];
        _more = false;
      }
    });
    try {
      final repo = ref.read(libraryRepositoryProvider);
      if (_albums) {
        final p = await repo.albums(page: page, query: query, sort: sort);
        if (!mounted || generation != _request) return;
        setState(() {
          _albumItems = append ? [..._albumItems, ...p.items] : p.items;
          _more = p.hasMore;
          _page = page;
        });
      } else {
        final localIds = <String>{};
        try {
          final local = await ref.read(localLibraryProvider.future);
          for (final track in await local.list()) {
            if (track.id.startsWith('song:') &&
                await local.hasDownloadedSong(track.id.substring(5))) {
              localIds.add(track.id);
            }
          }
        } catch (_) {
          if (downloaded) rethrow;
        }
        if (!mounted || generation != _request) return;
        var matches = <Song>[];
        var more = false;
        if (!downloaded || localIds.isNotEmpty) {
          do {
            final p = await repo.songs(
              page: page,
              query: query,
              provider: provider,
              sort: sort == 'order' ? 'recent' : sort,
              unorganized: unorganized ? true : null,
            );
            if (!mounted || generation != _request) return;
            matches = downloaded
                ? p.items
                      .where((s) => localIds.contains('song:${s.id}'))
                      .toList()
                : p.items;
            more = p.hasMore;
            if (matches.isEmpty && more) page++;
          } while (matches.isEmpty && more);
        }
        setState(() {
          _downloadedTrackIds = localIds;
          _songItems = append ? [..._songItems, ...matches] : matches;
          _more = more;
          _page = page;
        });
      }
    } catch (e) {
      if (mounted && generation == _request) setState(() => _error = e);
    } finally {
      if (mounted && generation == _request) setState(() => _busy = false);
    }
  }

  void _onSearchChanged(String _) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 320), _load);
    setState(() {});
  }

  String _sortLabel(String sort) => switch (sort) {
    'order' => 'sortOrder',
    'name' => 'sortName',
    _ => 'sortRecent',
  };

  void _selectFilter({String? section, String? provider}) {
    _debounce?.cancel();
    setState(() {
      if (section != null) {
        _section = section;
        if (section == 'albums') _provider = null;
      } else {
        if (_albums) _section = 'songs';
        _provider = _provider == provider ? null : provider;
      }
      if (!_albums) {
        _reordering = false;
        if (_sort == 'order') _sort = 'recent';
      }
    });
    _load();
  }

  Future<void> _importSharedAlbum() async {
    final album = await openImportSharedAlbumSheet(context);
    if (album == null || !mounted) return;
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(context.tr('sharedImportedNotice'))));
    await context.push('/albums/${album.id}');
  }

  Future<void> _moveAlbum(int index, int delta) async {
    final ids = _albumItems.map((a) => a.id).toList();
    final moved = ids.removeAt(index);
    ids.insert(index + delta, moved);
    setState(() => _busy = true);
    try {
      await ref.read(libraryRepositoryProvider).reorderAlbums(ids);
      ref.read(libraryRevisionProvider.notifier).changed();
      await _load();
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    ref.listen(libraryRevisionProvider, (_, _) => _load());
    ref.listen(
      sessionProvider.select((s) => s.value?.userId),
      (_, _) => _load(),
    );
    final scheme = Theme.of(context).colorScheme;
    final count = _albums ? _albumItems.length : _songItems.length;
    final textScale = MediaQuery.textScalerOf(context).scale(1);
    final canShowGrid =
        _albums &&
        !_reordering &&
        MediaQuery.sizeOf(context).width >= 350 &&
        textScale <= 1.35;

    return Scaffold(
      appBar: AppBar(
        leading: IconButton(
          tooltip: context.tr('profile'),
          onPressed: () => context.go('/profile'),
          icon: CircleAvatar(
            radius: 16,
            backgroundColor: scheme.primaryContainer,
            foregroundColor: scheme.onPrimaryContainer,
            child: const Icon(Icons.person_rounded, size: 19),
          ),
        ),
        titleSpacing: 0,
        title: Text(
          context.tr('library'),
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
        ),
        actions: [
          IconButton(
            tooltip: context.tr(_showSearch ? 'cancel' : 'search'),
            onPressed: () {
              setState(() => _showSearch = !_showSearch);
              if (!_showSearch && _query.text.isNotEmpty) {
                _debounce?.cancel();
                _query.clear();
                _load();
              }
            },
            icon: Icon(
              _showSearch ? Icons.close_rounded : Icons.search_rounded,
            ),
          ),
          IconButton(
            key: const Key('library-import-shared-album'),
            tooltip: context.tr('importSharedAlbum'),
            onPressed: _importSharedAlbum,
            icon: const Icon(Icons.link_rounded),
          ),
          IconButton(
            key: const Key('library-create-album'),
            tooltip: context.tr('createAlbum'),
            onPressed: () => context.push('/albums/new'),
            icon: const Icon(Icons.add_rounded),
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: _load,
        child: CustomScrollView(
          slivers: [
            SliverPadding(
              padding: const EdgeInsets.fromLTRB(18, 8, 18, 12),
              sliver: SliverToBoxAdapter(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const OfflineBanner(),
                    if (_showSearch) ...[
                      TextField(
                        autofocus: true,
                        controller: _query,
                        onChanged: _onSearchChanged,
                        onSubmitted: (_) {
                          _debounce?.cancel();
                          _load();
                        },
                        decoration: InputDecoration(
                          hintText: context.tr('searchHint'),
                          prefixIcon: const Icon(Icons.search_rounded),
                        ),
                      ),
                      const SizedBox(height: 12),
                    ],
                    SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: Row(
                        children: [
                          for (final (section, label) in [
                            ('albums', 'albums'),
                            ('songs', 'songs'),
                            ('downloaded', 'downloadedFilter'),
                          ])
                            Padding(
                              padding: const EdgeInsets.only(right: 8),
                              child: ChoiceChip(
                                label: Text(context.tr(label)),
                                selected: _section == section,
                                onSelected: (_) =>
                                    _selectFilter(section: section),
                              ),
                            ),
                          for (final provider in [
                            'youtube',
                            'tiktok',
                            'soundcloud',
                          ])
                            Padding(
                              padding: const EdgeInsets.only(right: 8),
                              child: FilterChip(
                                label: Text(providerDisplayLabel(provider)),
                                selected: _provider == provider,
                                onSelected: (_) =>
                                    _selectFilter(provider: provider),
                              ),
                            ),
                          ChoiceChip(
                            label: Text(context.tr('unclassified')),
                            selected: _unorganizedOnly,
                            onSelected: (_) =>
                                _selectFilter(section: 'unclassified'),
                          ),
                        ],
                      ),
                    ),
                    Row(
                      children: [
                        Flexible(
                          child: PopupMenuButton<String>(
                            tooltip: context.tr('sortLibrary'),
                            initialValue: _reordering ? 'reorder' : _sort,
                            onSelected: (value) {
                              setState(() {
                                if (value == 'reorder') {
                                  _reordering = !_reordering;
                                  _sort = 'order';
                                } else {
                                  _reordering = false;
                                  _sort = value;
                                }
                              });
                              _load();
                            },
                            itemBuilder: (context) => [
                              for (final value in [
                                'recent',
                                'name',
                                if (_albums) 'order',
                              ])
                                PopupMenuItem(
                                  value: value,
                                  child: Text(context.tr(_sortLabel(value))),
                                ),
                              if (_albums &&
                                  !_more &&
                                  _albumItems.length > 1 &&
                                  _query.text.trim().isEmpty)
                                PopupMenuItem(
                                  value: 'reorder',
                                  child: Text(
                                    context.tr(
                                      _reordering
                                          ? 'doneReorder'
                                          : 'reorderAlbums',
                                    ),
                                  ),
                                ),
                            ],
                            child: Padding(
                              padding: const EdgeInsets.symmetric(vertical: 12),
                              child: Row(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  const Icon(Icons.swap_vert_rounded, size: 20),
                                  const SizedBox(width: 6),
                                  Flexible(
                                    child: Text(context.tr(_sortLabel(_sort))),
                                  ),
                                ],
                              ),
                            ),
                          ),
                        ),
                        const Spacer(),
                        if (canShowGrid)
                          IconButton(
                            tooltip: context.tr(
                              _grid ? 'viewList' : 'viewGrid',
                            ),
                            onPressed: () => setState(() => _grid = !_grid),
                            icon: Icon(
                              _grid
                                  ? Icons.view_list_rounded
                                  : Icons.grid_view_rounded,
                            ),
                          ),
                      ],
                    ),
                    if (_albums && _query.text.trim().isEmpty && !_reordering)
                      _FavoriteSongsTile(
                        onTap: () => context.push('/favorites'),
                      ),
                    if (_busy) ...[
                      const SizedBox(height: 12),
                      const LinearProgressIndicator(),
                    ],
                    if (_error != null) ...[
                      Notice(errorText(context, _error!), error: true),
                      OutlinedButton(
                        onPressed: _load,
                        child: Text(context.tr('retry')),
                      ),
                    ],
                    if (!_busy && _error == null && count == 0)
                      EmptyView(
                        icon: _albums
                            ? Icons.album_rounded
                            : Icons.music_note_rounded,
                        title: context.tr(
                          _query.text.isNotEmpty
                              ? 'emptySearch'
                              : _downloadedOnly
                              ? 'emptyDownloaded'
                              : _albums
                              ? 'emptyAlbums'
                              : 'emptyLibrary',
                        ),
                        body: context.tr(
                          _query.text.isNotEmpty
                              ? 'emptySearchBody'
                              : _downloadedOnly
                              ? 'emptyDownloadedBody'
                              : _albums
                              ? 'emptyAlbumsBody'
                              : 'emptyLibraryBody',
                        ),
                        action: FilledButton(
                          onPressed: () => _albums
                              ? context.push('/albums/new')
                              : context.go('/'),
                          child: Text(
                            context.tr(
                              _albums ? 'createAlbum' : 'downloadMp3Primary',
                            ),
                          ),
                        ),
                      ),
                  ],
                ),
              ),
            ),
            if (_grid && canShowGrid && _albumItems.isNotEmpty)
              SliverPadding(
                padding: const EdgeInsets.symmetric(horizontal: 18),
                sliver: SliverGrid(
                  gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
                    maxCrossAxisExtent: 205,
                    mainAxisSpacing: 16,
                    crossAxisSpacing: 14,
                    childAspectRatio: 0.74,
                  ),
                  delegate: SliverChildBuilderDelegate(
                    (context, index) => AlbumGridCard(_albumItems[index]),
                    childCount: _albumItems.length,
                  ),
                ),
              )
            else
              SliverPadding(
                padding: const EdgeInsets.symmetric(horizontal: 14),
                sliver: SliverList.builder(
                  itemCount: count,
                  itemBuilder: (context, index) {
                    if (!_albums) {
                      final song = _songItems[index];
                      return SongTile(
                        song,
                        isDownloaded: _downloadedTrackIds.contains(
                          'song:${song.id}',
                        ),
                      );
                    }
                    final album = _albumItems[index];
                    return AlbumTile(
                      album,
                      trailing: _reordering
                          ? Row(
                              mainAxisSize: MainAxisSize.min,
                              children: [
                                IconButton(
                                  tooltip: context.tr('moveUp'),
                                  onPressed: _busy || index == 0
                                      ? null
                                      : () => _moveAlbum(index, -1),
                                  icon: const Icon(Icons.arrow_upward_rounded),
                                ),
                                IconButton(
                                  tooltip: context.tr('moveDown'),
                                  onPressed:
                                      _busy || index == _albumItems.length - 1
                                      ? null
                                      : () => _moveAlbum(index, 1),
                                  icon: const Icon(
                                    Icons.arrow_downward_rounded,
                                  ),
                                ),
                              ],
                            )
                          : null,
                    );
                  },
                ),
              ),
            if (_more)
              SliverPadding(
                padding: const EdgeInsets.all(20),
                sliver: SliverToBoxAdapter(
                  child: OutlinedButton(
                    onPressed: _busy ? null : () => _load(append: true),
                    child: Text(context.tr('more')),
                  ),
                ),
              ),
            const SliverToBoxAdapter(child: SizedBox(height: 36)),
          ],
        ),
      ),
    );
  }
}

class _FavoriteSongsTile extends StatelessWidget {
  const _FavoriteSongsTile({required this.onTap});
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => ListTile(
    key: const Key('library-favorite-songs'),
    contentPadding: EdgeInsets.zero,
    leading: Container(
      width: 54,
      height: 54,
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(6),
        gradient: const LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [Color(0xFF4932A8), Color(0xFF5378A9)],
        ),
      ),
      child: const Icon(Icons.favorite_rounded, color: Colors.white),
    ),
    title: Text(
      context.tr('favoriteSongs'),
      style: Theme.of(context).textTheme.titleMedium,
    ),
    trailing: const Icon(Icons.chevron_right_rounded),
    onTap: onTap,
  );
}
