import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/localization/app_copy.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/motion.dart';
import '../../shared/models/library_models.dart';
import '../library/library_repository.dart';
import '../library/library_screen.dart';

/// Unified Music Search Screen — Pill Search Bar + Grouped Album & Track Studio Surfaces.
class SearchScreen extends ConsumerStatefulWidget {
  const SearchScreen({super.key});

  @override
  ConsumerState<SearchScreen> createState() => _SearchScreenState();
}

class _SearchScreenState extends ConsumerState<SearchScreen> {
  final _query = TextEditingController();
  Timer? _debounce;
  SearchResults? _results;
  String _filter = 'all'; // 'all' | 'albums' | 'songs'
  bool _busy = false;
  Object? _error;
  int _generation = 0;

  @override
  void dispose() {
    _generation++;
    _debounce?.cancel();
    _query.dispose();
    super.dispose();
  }

  Future<void> _search() async {
    final term = _query.text.trim();
    if (term.isEmpty) {
      setState(() {
        _results = null;
        _error = null;
        _busy = false;
      });
      return;
    }
    final gen = ++_generation;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final res = await ref.read(libraryRepositoryProvider).search(term);
      if (mounted && gen == _generation) setState(() => _results = res);
    } catch (e) {
      if (mounted && gen == _generation) setState(() => _error = e);
    } finally {
      if (mounted && gen == _generation) setState(() => _busy = false);
    }
  }

  void _onChanged(String _) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 320), _search);
    setState(() {});
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final r = _results;
    final empty =
        r != null && r.albums.items.isEmpty && r.songs.items.isEmpty;
    final showAlbums = _filter == 'all' || _filter == 'albums';
    final showSongs = _filter == 'all' || _filter == 'songs';

    return Scaffold(
      appBar: AppBar(title: Text(context.tr('search'))),
      bottomNavigationBar: const SafeArea(top: false, child: MiniPlayerDock()),
      body: PageList(
        children: [
          TextField(
            key: const Key('global-search-input'),
            controller: _query,
            autofocus: true,
            onChanged: _onChanged,
            onSubmitted: (_) => _search(),
            decoration: InputDecoration(
              hintText: context.tr('searchHint'),
              prefixIcon: const Icon(Icons.search_rounded),
              border: OutlineInputBorder(
                borderRadius: BorderRadius.circular(999),
                borderSide: BorderSide.none,
              ),
              enabledBorder: OutlineInputBorder(
                borderRadius: BorderRadius.circular(999),
                borderSide: BorderSide(
                  color: scheme.outlineVariant.withValues(alpha: 0.65),
                ),
              ),
              focusedBorder: OutlineInputBorder(
                borderRadius: BorderRadius.circular(999),
                borderSide: BorderSide(color: scheme.primary, width: 1.8),
              ),
              suffixIcon: _query.text.isEmpty
                  ? null
                  : IconButton(
                      tooltip: context.tr('cancel'),
                      onPressed: () {
                        _query.clear();
                        _search();
                      },
                      icon: const Icon(Icons.close_rounded),
                    ),
            ),
          ),
          const SizedBox(height: 12),
          if (r != null && !empty) ...[
            SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              child: Row(
                children: [
                  ChoiceChip(
                    showCheckmark: false,
                    label: Text(context.tr('all')),
                    selected: _filter == 'all',
                    onSelected: (_) => setState(() => _filter = 'all'),
                  ),
                  const SizedBox(width: 8),
                  ChoiceChip(
                    showCheckmark: false,
                    label: Text(
                      '${context.tr('albums')} (${r.albums.total})',
                    ),
                    selected: _filter == 'albums',
                    onSelected: (_) => setState(() => _filter = 'albums'),
                  ),
                  const SizedBox(width: 8),
                  ChoiceChip(
                    showCheckmark: false,
                    label: Text('${context.tr('songs')} (${r.songs.total})'),
                    selected: _filter == 'songs',
                    onSelected: (_) => setState(() => _filter = 'songs'),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 14),
          ],
          if (_busy) const LinearProgressIndicator(),
          if (_error != null) ...[
            Notice(errorText(context, _error!), error: true),
            OutlinedButton(
              onPressed: _search,
              child: Text(context.tr('retry')),
            ),
          ],
          if (!_busy && _error == null && _query.text.trim().isEmpty)
            EmptyView(
              icon: Icons.search_rounded,
              title: context.tr('search'),
              body: context.tr('searchPrompt'),
            ),
          if (!_busy && _error == null && empty)
            EmptyView(
              icon: Icons.search_off_rounded,
              title: context.tr('emptySearch'),
              body: context.tr('emptySearchBody'),
            ),
          if (r != null) ...[
            if (showAlbums && r.albums.items.isNotEmpty) ...[
              Text(
                '${context.tr('albums')} (${r.albums.total})',
                style: Theme.of(context).textTheme.titleLarge,
              ),
              const SizedBox(height: 8),
              Material(
                color: scheme.surface,
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(18),
                  side: BorderSide(
                    color: scheme.outlineVariant.withValues(alpha: 0.6),
                  ),
                ),
                clipBehavior: Clip.antiAlias,
                child: Padding(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 8,
                    vertical: 6,
                  ),
                  child: Column(
                    children: r.albums.items.map(AlbumTile.new).toList(),
                  ),
                ),
              ),
              const SizedBox(height: 20),
            ],
            if (showSongs && r.songs.items.isNotEmpty) ...[
              Text(
                '${context.tr('songs')} (${r.songs.total})',
                style: Theme.of(context).textTheme.titleLarge,
              ),
              const SizedBox(height: 8),
              Material(
                color: scheme.surface,
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(18),
                  side: BorderSide(
                    color: scheme.outlineVariant.withValues(alpha: 0.6),
                  ),
                ),
                clipBehavior: Clip.antiAlias,
                child: Padding(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 8,
                    vertical: 6,
                  ),
                  child: Column(
                    children: r.songs.items.map(SongTile.new).toList(),
                  ),
                ),
              ),
            ],
          ],
        ],
      ),
    );
  }
}

/// Favorites Screen — Favorite Albums & Favorite Songs inside Studio Surfaces.
class FavoritesScreen extends ConsumerStatefulWidget {
  const FavoritesScreen({super.key});

  @override
  ConsumerState<FavoritesScreen> createState() => _FavoritesScreenState();
}

class _FavoritesScreenState extends ConsumerState<FavoritesScreen> {
  late Future<(PageResult<Album>, PageResult<Song>)> _favorites;

  @override
  void initState() {
    super.initState();
    _favorites = _load();
  }

  Future<(PageResult<Album>, PageResult<Song>)> _load() async {
    final repo = ref.read(libraryRepositoryProvider);
    final albums = await repo.albums(favorite: true);
    final songs = await repo.songs(favorite: true);
    return (albums, songs);
  }

  Future<void> _refresh() async {
    final next = _load();
    setState(() {
      _favorites = next;
    });
    await next;
  }

  @override
  Widget build(BuildContext context) {
    ref.listen(libraryRevisionProvider, (_, _) {
      final next = _load();
      setState(() {
        _favorites = next;
      });
    });
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      appBar: AppBar(title: Text(context.tr('favorites'))),
      bottomNavigationBar: const SafeArea(top: false, child: MiniPlayerDock()),
      body: RefreshIndicator(
        onRefresh: _refresh,
        child: FutureBuilder<(PageResult<Album>, PageResult<Song>)>(
          future: _favorites,
          builder: (context, snapshot) {
            if (snapshot.hasError) {
              return LoadFailure(error: snapshot.error!, retry: _refresh);
            }
            if (!snapshot.hasData) {
              return const Center(child: CircularProgressIndicator());
            }
            final (albums, songs) = snapshot.data!;
            final isEmpty = albums.items.isEmpty && songs.items.isEmpty;
            return PageList(
              children: [
                const OfflineBanner(),
                if (isEmpty)
                  EmptyView(
                    icon: Icons.favorite_border_rounded,
                    title: context.tr('emptyFavorites'),
                    body: context.tr('emptyFavoritesBody'),
                  ),
                if (albums.items.isNotEmpty) ...[
                  Text(
                    context.tr('favoriteAlbums'),
                    style: Theme.of(context).textTheme.titleLarge,
                  ),
                  const SizedBox(height: 10),
                  Material(
                    color: scheme.surface,
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(18),
                      side: BorderSide(
                        color: scheme.outlineVariant.withValues(alpha: 0.6),
                      ),
                    ),
                    clipBehavior: Clip.antiAlias,
                    child: Padding(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 8,
                        vertical: 6,
                      ),
                      child: Column(
                        children: albums.items.map(AlbumTile.new).toList(),
                      ),
                    ),
                  ),
                ],
                if (albums.items.isNotEmpty && songs.items.isNotEmpty)
                  const SizedBox(height: 22),
                if (songs.items.isNotEmpty) ...[
                  Text(
                    context.tr('favoriteSongs'),
                    style: Theme.of(context).textTheme.titleLarge,
                  ),
                  const SizedBox(height: 10),
                  Material(
                    color: scheme.surface,
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(18),
                      side: BorderSide(
                        color: scheme.outlineVariant.withValues(alpha: 0.6),
                      ),
                    ),
                    clipBehavior: Clip.antiAlias,
                    child: Padding(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 8,
                        vertical: 6,
                      ),
                      child: Column(
                        children: songs.items.map(SongTile.new).toList(),
                      ),
                    ),
                  ),
                ],
              ],
            );
          },
        ),
      ),
    );
  }
}
