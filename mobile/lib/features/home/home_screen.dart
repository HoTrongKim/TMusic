import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../app/theme/tmusic_theme.dart';
import '../../core/auth/session.dart';
import '../../core/localization/app_copy.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/motion.dart';
import '../../core/widgets/swipe_song_tile.dart';
import '../../shared/models/library_models.dart';
import '../albums/album_share_sheets.dart';
import '../library/library_repository.dart';
import '../library/library_screen.dart';
import '../playback/local_audio.dart';
import '../profile/profile_screens.dart';
import '../settings/settings_controller.dart';
import 'smart_download_card.dart';

class _HomeData {
  const _HomeData({
    required this.recentSongs,
    required this.favoriteAlbums,
    required this.recentAlbums,
    required this.unorganizedSongs,
  });

  final List<Song> recentSongs;
  final List<Album> favoriteAlbums;
  final List<Album> recentAlbums;
  final List<Song> unorganizedSongs;
}

/// Spotify-Inspired Home Screen (matching Reference Screenshots 1 & 4):
/// Time-of-day Greeting Header -> 2x3 Bento Quick-Pick Grid -> Smart Link-to-MP3 Card ->
/// Featured Release Hero Card ("NEW FROM SOURCE") -> Horizontal Square Cover Carousels.
class HomeScreen extends ConsumerStatefulWidget {
  const HomeScreen({super.key, this.albumId});
  final String? albumId;

  @override
  ConsumerState<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends ConsumerState<HomeScreen> {
  late Future<_HomeData> _content;
  bool _changingTheme = false;

  @override
  void initState() {
    super.initState();
    _content = _load();
  }

  Future<_HomeData> _load() async {
    final repo = ref.read(libraryRepositoryProvider);
    final songs = await repo.songs();
    final unorganized = await repo.songs(unorganized: true);
    final favAlbums = await repo.albums(favorite: true);
    final recentAlbums = await repo.recentOpenedAlbums();
    return _HomeData(
      recentSongs: songs.items.take(8).toList(),
      favoriteAlbums: favAlbums.items.take(8).toList(),
      recentAlbums: recentAlbums.take(6).toList(),
      unorganizedSongs: unorganized.items.take(4).toList(),
    );
  }

  Future<void> _refresh() async {
    final next = _load();
    setState(() {
      _content = next;
    });
    await next;
  }

  Future<void> _toggleTheme() async {
    if (_changingTheme) return;
    final next = Theme.of(context).brightness == Brightness.dark
        ? 'light'
        : 'dark';
    setState(() => _changingTheme = true);
    try {
      await ref.read(settingsProvider.notifier).save(theme: next);
      TMusicHaptics.light();
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(errorText(context, e))));
      }
    } finally {
      if (mounted) setState(() => _changingTheme = false);
    }
  }

  Future<void> _quickPlaySong(Song song) async {
    try {
      final local = await ref.read(localLibraryProvider.future);
      final track = song.isGuestLocal
          ? await local.getDownloadedSongTrack(song)
          : await local.ensureSongTrack(
              song,
              () => ref
                  .read(libraryRepositoryProvider)
                  .fetchSongAudioBytes(song.id),
            );
      if (track == null) {
        if (mounted) context.push('/songs/${song.id}');
        return;
      }
      final player = await ref.read(localAudioProvider.future);
      TMusicHaptics.light();
      await player.start([track], 0);
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(errorText(context, e))));
      }
    }
  }

  String _spotifyGreetingKey() {
    final h = DateTime.now().hour;
    if (h < 12) return 'goodMorning';
    if (h < 18) return 'goodAfternoon';
    return 'goodEvening';
  }

  @override
  Widget build(BuildContext context) {
    ref.listen<String?>(
      sessionProvider.select((value) => value.value?.userId),
      (previous, next) {
        if (previous == next) return;
        final content = _load();
        setState(() {
          _content = content;
        });
      },
    );
    ref.listen(libraryRevisionProvider, (_, _) {
      final next = _load();
      setState(() {
        _content = next;
      });
    });

    final session = ref.watch(sessionProvider).value;
    final isGuest = session == null;
    final profileAsync = isGuest ? null : ref.watch(profileProvider);
    final profile = profileAsync?.hasValue == true ? profileAsync!.value : null;
    final displayName = (profile?['displayName'] as String? ?? '').trim();
    final avatarUrl = profile?['avatarUrl'] as String?;
    final scheme = Theme.of(context).colorScheme;
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final settings = ref.watch(settingsProvider);
    final canChangeTheme =
        settings.hasValue && !settings.isLoading && !settings.hasError;

    return SwipeActionGroup(
      child: Scaffold(
      body: Builder(
        builder: (bodyCtx) => NotificationListener<ScrollStartNotification>(
          onNotification: (_) {
            SwipeActionGroup.closeAll(bodyCtx);
            return false;
          },
          child: DecoratedBox(
        decoration: BoxDecoration(
          gradient: RadialGradient(
            center: const Alignment(-0.85, -1.0),
            radius: 1.15,
            colors: [
              isDark
                  ? const Color(0xFF233D2E)
                  : TMusicTheme.spotifyGreen.withValues(alpha: 0.14),
              scheme.surfaceContainerLowest,
            ],
          ),
        ),
        child: RefreshIndicator(
          color: TMusicTheme.spotifyGreen,
          onRefresh: _refresh,
          child: PageList(
            children: [
              // Greeting, shared albums, search, appearance and profile.
              Row(
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          context.tr(_spotifyGreetingKey()),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: Theme.of(context).textTheme.headlineLarge
                              ?.copyWith(fontSize: 24),
                        ),
                        if (displayName.isNotEmpty)
                          Text(
                            displayName,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: Theme.of(context).textTheme.bodySmall,
                          ),
                      ],
                    ),
                  ),
                  IconButton(
                    key: const Key('home-import-shared'),
                    tooltip: context.tr('importSharedAlbum'),
                    onPressed: () async {
                      final imported = await openImportSharedAlbumSheet(
                        context,
                      );
                      if (imported != null && context.mounted) {
                        context.push('/albums/${imported.id}');
                      }
                    },
                    icon: const Icon(Icons.group_add_outlined),
                  ),
                  IconButton(
                    tooltip: context.tr('search'),
                    onPressed: () => context.push('/search'),
                    icon: const Icon(Icons.search_rounded),
                  ),
                  IconButton(
                    key: const Key('home-theme-toggle'),
                    tooltip: context.tr(
                      isDark ? 'switchToLight' : 'switchToDark',
                    ),
                    onPressed: _changingTheme || !canChangeTheme
                        ? null
                        : _toggleTheme,
                    icon: Icon(
                      isDark
                          ? Icons.light_mode_outlined
                          : Icons.dark_mode_outlined,
                    ),
                  ),
                  IconButton(
                    key: const Key('home-avatar-shortcut'),
                    tooltip: context.tr(isGuest ? 'login' : 'profile'),
                    onPressed: () => context.go('/profile'),
                    icon: avatarUrl != null && avatarUrl.isNotEmpty
                        ? Cover(url: avatarUrl, size: 30, radius: 15)
                        : Container(
                            width: 30,
                            height: 30,
                            decoration: BoxDecoration(
                              shape: BoxShape.circle,
                              color: isGuest
                                  ? scheme.surfaceContainerLow
                                  : TMusicTheme.spotifyGreen,
                            ),
                            child: Icon(
                              Icons.person_rounded,
                              size: 18,
                              color: isGuest ? scheme.onSurface : Colors.black,
                            ),
                          ),
                  ),
                ],
              ),
              const SizedBox(height: 10),
              const OfflineBanner(),

              // Guest Local Mode Compact Pill Notice
              if (isGuest) ...[
                Container(
                  margin: const EdgeInsets.only(bottom: 14),
                  padding: const EdgeInsets.fromLTRB(12, 9, 10, 9),
                  decoration: BoxDecoration(
                    color: scheme.surfaceContainerLow,
                    borderRadius: BorderRadius.circular(10),
                  ),
                  child: Wrap(
                    alignment: WrapAlignment.spaceBetween,
                    crossAxisAlignment: WrapCrossAlignment.center,
                    spacing: 8,
                    runSpacing: 6,
                    children: [
                      Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          const Icon(
                            Icons.smartphone_rounded,
                            size: 17,
                            color: TMusicTheme.spotifyGreen,
                          ),
                          const SizedBox(width: 8),
                          Flexible(
                            child: Text(
                              context.tr('guestModeBanner'),
                              style: Theme.of(context).textTheme.bodySmall
                                  ?.copyWith(fontSize: 12),
                            ),
                          ),
                        ],
                      ),
                      FilledButton(
                        key: const Key('home-guest-login'),
                        style: FilledButton.styleFrom(
                          minimumSize: const Size(48, 34),
                          padding: const EdgeInsets.symmetric(
                            horizontal: 14,
                            vertical: 6,
                          ),
                          textStyle: const TextStyle(
                            fontSize: 12.5,
                            fontWeight: FontWeight.w800,
                          ),
                        ),
                        onPressed: () => context.push('/auth/login'),
                        child: Text(context.tr('guestLoginCta')),
                      ),
                    ],
                  ),
                ),
              ],

              // 2. Dynamic Spotify Content + Link-to-MP3 Card
              FutureBuilder<_HomeData>(
                key: ValueKey(session?.userId),
                future: _content,
                builder: (context, snapshot) {
                  if (snapshot.hasError) {
                    return Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        SmartDownloadCard(initialAlbumId: widget.albumId),
                        const SizedBox(height: 16),
                        Notice(
                          errorText(context, snapshot.error!),
                          error: true,
                        ),
                        OutlinedButton(
                          onPressed: _refresh,
                          child: Text(context.tr('retry')),
                        ),
                      ],
                    );
                  }
                  if (!snapshot.hasData) {
                    return Column(
                      children: [
                        SmartDownloadCard(initialAlbumId: widget.albumId),
                        const SizedBox(height: 16),
                        const LinearProgressIndicator(),
                      ],
                    );
                  }

                  final data = snapshot.data!;
                  final bentoAlbums = data.recentAlbums.isNotEmpty
                      ? data.recentAlbums
                      : data.favoriteAlbums.take(6).toList();

                  return Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      // Spotify 2x3 Bento Quick-Pick Grid (Screenshots 1 & 4)
                      if (bentoAlbums.isNotEmpty) ...[
                        _SpotifyBentoGrid(albums: bentoAlbums),
                        const SizedBox(height: 18),
                      ],

                      // Link-to-MP3 Studio Card (YouTube · TikTok · SoundCloud)
                      SmartDownloadCard(initialAlbumId: widget.albumId),
                      const SizedBox(height: 22),

                      // Spotify "NEW RELEASE / FEATURED SOURCE" Card (Screenshot #1)
                      if (data.recentSongs.isNotEmpty) ...[
                        _SpotifyFeaturedSongCard(
                          song: data.recentSongs.first,
                          onPlay: () => _quickPlaySong(data.recentSongs.first),
                        ),
                        const SizedBox(height: 24),
                      ],

                      // Horizontal Square Cover Carousel ("Recently played" - Screenshot #4)
                      if (data.favoriteAlbums.isNotEmpty) ...[
                        Text(
                          context.tr('favoriteAlbums'),
                          style: Theme.of(context).textTheme.titleLarge,
                        ),
                        const SizedBox(height: 12),
                        SizedBox(
                          height: 196,
                          child: ListView.separated(
                            scrollDirection: Axis.horizontal,
                            itemCount: data.favoriteAlbums.length,
                            separatorBuilder: (_, _) =>
                                const SizedBox(width: 14),
                            itemBuilder: (context, i) =>
                                _SpotifyCarouselCard(data.favoriteAlbums[i]),
                          ),
                        ),
                        const SizedBox(height: 24),
                      ],

                      if (data.recentSongs.isEmpty)
                        EmptyView(
                          icon: Icons.library_music_rounded,
                          title: context.tr('emptyLibrary'),
                          body: context.tr('emptyLibraryBody'),
                        )
                      else ...[
                        Text(
                          context.tr('recent'),
                          style: Theme.of(context).textTheme.titleLarge,
                        ),
                        const SizedBox(height: 8),
                        ...data.recentSongs.map(
                          (s) => SongTile(
                            s,
                            onChanged: _refresh,
                            trailing: s.audioReady
                                ? IconButton(
                                    tooltip: context.tr('playMp3'),
                                    onPressed: () => _quickPlaySong(s),
                                    icon: const Icon(
                                      Icons.play_circle_fill_rounded,
                                      size: 30,
                                      color: TMusicTheme.spotifyGreen,
                                    ),
                                  )
                                : null,
                          ),
                        ),
                      ],

                      if (data.unorganizedSongs.isNotEmpty) ...[
                        const SizedBox(height: 20),
                        Text(
                          context.tr('continueOrganizing'),
                          style: Theme.of(context).textTheme.titleLarge,
                        ),
                        const SizedBox(height: 8),
                        ...data.unorganizedSongs.map(
                          (s) => SongTile(s, onChanged: _refresh),
                        ),
                      ],
                    ],
                  );
                },
              ),
            ],
          ),
        ),
      ),
      ),
      ),
    ),
    );
  }
}

/// Spotify's Signature 2-Column Horizontal Bento Quick-Pick Cards (Screenshots 1 & 4).
class _SpotifyBentoGrid extends StatelessWidget {
  const _SpotifyBentoGrid({required this.albums});
  final List<Album> albums;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final textScale = MediaQuery.textScalerOf(context).scale(1);
    return LayoutBuilder(
      builder: (context, constraints) {
        final twoCols = constraints.maxWidth >= 340 && textScale <= 1.35;
        if (!twoCols) {
          return Column(children: albums.take(4).map(AlbumTile.new).toList());
        }
        final itemWidth = (constraints.maxWidth - 8) / 2;
        return Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            for (final a in albums.take(6))
              SizedBox(
                width: itemWidth,
                child: Material(
                  color: scheme.surfaceContainerLow,
                  borderRadius: BorderRadius.circular(6),
                  clipBehavior: Clip.antiAlias,
                  child: InkWell(
                    onTap: () => context.push('/albums/${a.id}'),
                    child: Row(
                      children: [
                        Cover(url: a.coverImageUrl, size: 56, radius: 0),
                        const SizedBox(width: 8),
                        Expanded(
                          child: Padding(
                            padding: const EdgeInsets.only(right: 8),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  a.name,
                                  maxLines: 2,
                                  overflow: TextOverflow.ellipsis,
                                  style: Theme.of(context).textTheme.labelLarge
                                      ?.copyWith(fontSize: 12.5),
                                ),
                                if (a.songCount > 0) ...[
                                  const SizedBox(height: 4),
                                  Container(
                                    width: 32,
                                    height: 3,
                                    decoration: BoxDecoration(
                                      color: TMusicTheme.spotifyGreen,
                                      borderRadius: BorderRadius.circular(2),
                                    ),
                                  ),
                                ],
                              ],
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
          ],
        );
      },
    );
  }
}

/// Spotify "NEW RELEASE FROM..." Featured Hero Card (Screenshot #1) with Explicit Source Attribution.
class _SpotifyFeaturedSongCard extends StatelessWidget {
  const _SpotifyFeaturedSongCard({required this.song, required this.onPlay});

  final Song song;
  final VoidCallback onPlay;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Cover(url: song.thumbnail, size: 38, radius: 19),
            const SizedBox(width: 10),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    song.sourceAttribution.toUpperCase(),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(context).textTheme.labelSmall,
                  ),
                  Text(
                    song.artist ?? song.originalCreator ?? song.providerLabel,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(context).textTheme.titleMedium
                        ?.copyWith(fontWeight: FontWeight.w800),
                  ),
                ],
              ),
            ),
          ],
        ),
        const SizedBox(height: 10),
        Material(
          color: scheme.surfaceContainerLow,
          borderRadius: BorderRadius.circular(10),
          clipBehavior: Clip.antiAlias,
          child: InkWell(
            onTap: () => context.push('/songs/${song.id}'),
            child: Padding(
              padding: const EdgeInsets.all(12),
              child: Row(
                children: [
                  Cover(url: song.thumbnail, size: 80, radius: 4),
                  const SizedBox(width: 14),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          song.title,
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                          style: Theme.of(context).textTheme.titleMedium
                              ?.copyWith(fontWeight: FontWeight.w800),
                        ),
                        const SizedBox(height: 4),
                        ProviderBadge(provider: song.provider),
                      ],
                    ),
                  ),
                  const SizedBox(width: 8),
                  SizedBox(
                    width: 42,
                    height: 42,
                    child: Material(
                      color: song.audioReady
                          ? TMusicTheme.spotifyGreen
                          : scheme.surfaceContainerHigh,
                      shape: const CircleBorder(),
                      child: InkWell(
                        customBorder: const CircleBorder(),
                        onTap: onPlay,
                        child: Icon(
                          song.audioReady
                              ? Icons.play_arrow_rounded
                              : Icons.arrow_forward_rounded,
                          color: song.audioReady
                              ? Colors.black
                              : scheme.onSurface,
                        ),
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ],
    );
  }
}

class _SpotifyCarouselCard extends StatelessWidget {
  const _SpotifyCarouselCard(this.album);
  final Album album;

  @override
  Widget build(BuildContext context) => SizedBox(
    width: 144,
    child: InkWell(
      borderRadius: BorderRadius.circular(8),
      onTap: () => context.push('/albums/${album.id}'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Cover(url: album.coverImageUrl, size: 144, radius: 6),
          const SizedBox(height: 8),
          Text(
            album.name,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: Theme.of(context).textTheme.labelLarge
                ?.copyWith(fontSize: 13.5),
          ),
          const SizedBox(height: 2),
          Text(
            'Album • ${album.songCount} ${context.tr('songs').toLowerCase()}',
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: Theme.of(context).textTheme.bodySmall,
          ),
        ],
      ),
    ),
  );
}
