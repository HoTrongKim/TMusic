import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../core/auth/session.dart';
import '../core/localization/app_copy.dart';
import '../core/widgets/motion.dart';
import '../features/albums/album_screens.dart';
import '../features/authentication/auth_screens.dart';
import '../features/home/home_screen.dart';
import '../features/library/library_repository.dart';
import '../features/library/library_screen.dart';
import '../features/link_import/import_screens.dart';
import '../features/playback/downloads_screen.dart';
import '../features/playback/local_audio_screen.dart';
import '../features/profile/profile_screens.dart';
import '../features/search/search_screen.dart';
import '../features/settings/settings_controller.dart';
import '../features/share/shared_links.dart';
import '../features/songs/song_screen.dart';
import 'theme/tmusic_theme.dart';

class TMusicApp extends ConsumerStatefulWidget {
  const TMusicApp({super.key, this.initialLocation = '/'});
  final String initialLocation;

  @override
  ConsumerState<TMusicApp> createState() => _TMusicAppState();
}

class _TMusicAppState extends ConsumerState<TMusicApp>
    with WidgetsBindingObserver {
  final _authChanged = _AuthRefresh();
  final _rootNavigatorKey = GlobalKey<NavigatorState>(debugLabel: 'root');
  String? _pendingProtectedLocation;

  static bool _requiresAccount(String path) =>
      path == '/security' ||
      path == '/sessions' ||
      path == '/account/delete' ||
      path == '/imports' ||
      path.startsWith('/imports/');

  late final GoRouter _router = GoRouter(
    navigatorKey: _rootNavigatorKey,
    initialLocation: widget.initialLocation,
    refreshListenable: _authChanged,
    redirect: (context, state) {
      final session = ref.read(sessionProvider);
      final path = state.uri.path;
      final auth = path == '/welcome' || path.startsWith('/auth/');
      if (session.isLoading || session.hasError) {
        if (!auth && path != '/loading' && path != '/') {
          _pendingProtectedLocation = state.uri.toString();
        }
        return path == '/loading' ? null : '/loading';
      }
      if (session.value == null) {
        if (path == '/loading') {
          final init = widget.initialLocation;
          // A public startup route is only a restore destination, never the
          // target of a later sign-in initiated from that guest screen.
          if (!_requiresAccount(init)) _pendingProtectedLocation = null;
          if (init != '/loading') {
            return _requiresAccount(init) ? '/auth/login' : init;
          }
          return '/';
        }
        if (_requiresAccount(path)) {
          _pendingProtectedLocation = state.uri.toString();
          return '/auth/login';
        }
        return null; // Guest Local Mode enabled for Home, Library, Albums, Songs, Downloads, Profile!
      }
      if (auth || path == '/loading') {
        if (ref.read(sharedLinksProvider).isNotEmpty) {
          _pendingProtectedLocation = null;
          return '/';
        }
        final target = _pendingProtectedLocation;
        _pendingProtectedLocation = null;
        return target ?? '/';
      }
      return null;
    },
    routes: [
      GoRoute(
        path: '/loading',
        builder: (_, _) => const SessionLoadingScreen(),
      ),
      GoRoute(path: '/welcome', builder: (_, _) => const WelcomeScreen()),
      GoRoute(
        path: '/auth/:mode',
        builder: (_, state) => AuthScreen(
          mode: state.pathParameters['mode']!,
          email: state.uri.queryParameters['email'] ?? '',
        ),
      ),
      GoRoute(path: '/library', redirect: (_, _) => '/albums'),
      GoRoute(
        path: '/add',
        redirect: (_, s) {
          final album = s.uri.queryParameters['album'];
          return album != null && album.isNotEmpty
              ? '/?album=${Uri.encodeQueryComponent(album)}'
              : '/';
        },
      ),
      StatefulShellRoute.indexedStack(
        builder: (_, _, shell) => _Shell(shell),
        branches: [
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/',
                builder: (_, s) =>
                    HomeScreen(albumId: s.uri.queryParameters['album']),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/albums',
                builder: (_, _) => const LibraryScreen(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/downloads',
                builder: (_, _) => const DownloadsScreen(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/profile',
                builder: (_, _) => const ProfileScreen(),
              ),
            ],
          ),
        ],
      ),
      GoRoute(
        parentNavigatorKey: _rootNavigatorKey,
        path: '/albums/new',
        builder: (_, _) => const AlbumEditor(),
      ),
      GoRoute(
        parentNavigatorKey: _rootNavigatorKey,
        path: '/albums/:id/edit',
        builder: (_, s) => AlbumEditor(id: s.pathParameters['id']),
      ),
      GoRoute(
        parentNavigatorKey: _rootNavigatorKey,
        path: '/albums/:id',
        builder: (_, s) => AlbumDetailScreen(id: s.pathParameters['id']!),
      ),
      GoRoute(
        parentNavigatorKey: _rootNavigatorKey,
        path: '/songs/:id/edit',
        builder: (_, s) => EditSongScreen(id: s.pathParameters['id']!),
      ),
      GoRoute(
        parentNavigatorKey: _rootNavigatorKey,
        path: '/songs/:id',
        builder: (_, s) => SongScreen(id: s.pathParameters['id']!),
      ),
      GoRoute(
        parentNavigatorKey: _rootNavigatorKey,
        path: '/playback/:id',
        builder: (_, s) => SongScreen(id: s.pathParameters['id']!),
      ),
      GoRoute(
        parentNavigatorKey: _rootNavigatorKey,
        path: '/favorites',
        builder: (_, _) => const FavoritesScreen(),
      ),
      GoRoute(
        parentNavigatorKey: _rootNavigatorKey,
        path: '/search',
        builder: (_, _) => const SearchScreen(),
      ),
      GoRoute(
        parentNavigatorKey: _rootNavigatorKey,
        path: '/settings',
        builder: (_, _) => const SettingsScreen(),
      ),
      GoRoute(
        parentNavigatorKey: _rootNavigatorKey,
        path: '/security',
        builder: (_, _) => const SecurityScreen(),
      ),
      GoRoute(
        parentNavigatorKey: _rootNavigatorKey,
        path: '/account/delete',
        builder: (_, _) => const DeleteAccountScreen(),
      ),
      GoRoute(
        parentNavigatorKey: _rootNavigatorKey,
        path: '/sessions',
        builder: (_, _) => const SessionsScreen(),
      ),
      GoRoute(
        parentNavigatorKey: _rootNavigatorKey,
        path: '/privacy',
        builder: (_, _) => const PrivacyScreen(),
      ),
      GoRoute(
        parentNavigatorKey: _rootNavigatorKey,
        path: '/imports',
        builder: (_, _) => const ImportsScreen(),
      ),
      GoRoute(
        parentNavigatorKey: _rootNavigatorKey,
        path: '/imports/previews/:id',
        builder: (_, s) =>
            ImportStatusScreen(id: s.pathParameters['id']!, preview: true),
      ),
      GoRoute(
        parentNavigatorKey: _rootNavigatorKey,
        path: '/imports/jobs/:id',
        builder: (_, s) =>
            ImportStatusScreen(id: s.pathParameters['id']!, preview: false),
      ),
      GoRoute(
        parentNavigatorKey: _rootNavigatorKey,
        path: '/local',
        builder: (_, _) => const LocalAudioScreen(),
      ),
    ],
  );

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    SharedLinks.channel.setMethodCallHandler((call) async {
      if (call.method == 'available') await _share();
    });
    WidgetsBinding.instance.addPostFrameCallback((_) => _share());
  }

  Future<void> _share() async {
    if (await ref.read(sharedLinksProvider.notifier).receive() && mounted) {
      final links = ref.read(sharedLinksProvider);
      if (links.isNotEmpty) {
        ref.read(linkDraftProvider.notifier).update(url: links.first);
      }
      _router.go('/');
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      _share();
      ref.read(libraryRevisionProvider.notifier).changed();
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    SharedLinks.channel.setMethodCallHandler(null);
    _router.dispose();
    _authChanged.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    ref.listen(sessionProvider, (prev, next) {
      _authChanged.refresh();
      // Auto-sync any Guest local albums & songs into the user's cloud account upon sign-in!
      if ((prev == null || prev.value == null) && next.value != null) {
        final uid = next.value!.userId;
        unawaited(
          ref
              .read(libraryRepositoryProvider)
              .syncGuestDataToCloud(userId: uid)
              .then((synced) {
                if (synced && mounted) {
                  ref.read(libraryRevisionProvider.notifier).changed();
                }
              }),
        );
      }
    });
    final settingsAsync = ref.watch(settingsProvider);
    final settings = settingsAsync.hasValue
        ? (settingsAsync.value ?? const UserSettings())
        : const UserSettings();
    return MaterialApp.router(
      title: 'TMusic',
      debugShowCheckedModeBanner: false,
      locale: Locale(settings.language),
      supportedLocales: const [Locale('vi'), Locale('en')],
      localizationsDelegates: GlobalMaterialLocalizations.delegates,
      theme: TMusicTheme.light,
      darkTheme: TMusicTheme.dark,
      themeMode: settings.mode,
      routerConfig: _router,
    );
  }
}

class _Shell extends ConsumerWidget {
  const _Shell(this.shell);
  final StatefulNavigationShell shell;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final activeDownloads = ref.watch(activeDownloadCountProvider);
    final isDark = Theme.of(context).brightness == Brightness.dark;
    const keys = ['home', 'albums', 'downloads', 'profile'];
    const labels = ['home', 'library', 'downloads', 'profile'];
    const icons = [
      Icons.home_outlined,
      Icons.library_music_outlined,
      Icons.downloading_outlined,
      Icons.person_outline_rounded,
    ];
    const selectedIcons = [
      Icons.home_filled,
      Icons.library_music_rounded,
      Icons.download_for_offline_rounded,
      Icons.person_rounded,
    ];

    Widget wrapDownloadsBadge(Widget icon) => Badge(
      backgroundColor: TMusicTheme.spotifyGreen,
      textColor: Colors.black,
      isLabelVisible: activeDownloads > 0,
      label: Text('$activeDownloads'),
      child: icon,
    );

    return Scaffold(
      body: SafeArea(child: shell),
      bottomNavigationBar: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const MiniPlayerDock(),
          SpotifyGlassSurface(
            tint: isDark ? const Color(0xE6121212) : const Color(0xF2FFFFFF),
            border: Border(
              top: BorderSide(
                color: (isDark ? Colors.white : Colors.black).withValues(
                  alpha: 0.06,
                ),
              ),
            ),
            child: NavigationBar(
              selectedIndex: shell.currentIndex,
              onDestinationSelected: (i) {
                TMusicHaptics.selection();
                shell.goBranch(i, initialLocation: i == shell.currentIndex);
              },
              destinations: List.generate(4, (i) {
                final icon = Icon(icons[i]);
                final selectedIcon = Icon(selectedIcons[i]);
                return NavigationDestination(
                  key: Key('nav-${keys[i]}'),
                  icon: i == 2 ? wrapDownloadsBadge(icon) : icon,
                  selectedIcon: i == 2
                      ? wrapDownloadsBadge(selectedIcon)
                      : selectedIcon,
                  label: context.tr(labels[i]),
                );
              }),
            ),
          ),
        ],
      ),
    );
  }
}

class _AuthRefresh extends ChangeNotifier {
  void refresh() => notifyListeners();
}
