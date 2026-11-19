import 'package:audio_service/audio_service.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:tmusic/app/tmusic_app.dart';
import 'package:tmusic/core/api/api_client.dart';
import 'package:tmusic/core/auth/session.dart';
import 'package:tmusic/core/storage/guest_library_store.dart';
import 'package:tmusic/features/library/library_repository.dart';
import 'package:tmusic/features/playback/local_audio.dart';
import 'package:tmusic/features/playback/local_audio_screen.dart';
import 'package:tmusic/features/settings/settings_controller.dart';

class _ActiveSession extends SessionController {
  @override
  Future<Session?> build() async => Session.fromJson({
    'accessToken': 'token-v2',
    'refreshToken': 'refresh-v2',
    'userId': 'user-v2',
    'expiresAt': DateTime.now().add(const Duration(hours: 1)).toIso8601String(),
  });
}

class _GuestSession extends SessionController {
  @override
  Future<Session?> build() async => null;
}

class _DefaultSettings extends SettingsController {
  @override
  Future<UserSettings> build() async => const UserSettings();
}

class _DownloadedLibrary extends LocalLibrary {
  _DownloadedLibrary() : super(null, null);
  List<LocalTrack> tracks = const [
    LocalTrack('song:cloud-song', 'Mây trên phố', '/local-track.mp3'),
  ];
  List<LocalTrack> albumTracks = const [];
  String? addedTrackId;
  String? addedAlbumId;

  @override
  Future<List<LocalTrack>> list() async => tracks;

  @override
  Future<bool> hasDownloadedSong(String songId) async => songId == 'cloud-song';

  @override
  Future<List<LocalTrack>> listAlbumTracks(String albumId) async => albumTracks;

  @override
  Future<void> addTrackToAlbum(String trackId, String albumId) async {
    addedTrackId = trackId;
    addedAlbumId = albumId;
  }
}

class _ScenarioApi extends ApiClient {
  _ScenarioApi({this.failFavorite = false});
  bool failFavorite;
  String songTitle = 'Bản Acoustic Đêm';
  bool isFav = true;
  bool shareRevoked = false;
  bool sharedImported = false;
  bool multipleSongPages = false;
  String? songProviderFilter;
  String? albumSongProviderFilter;
  static const _sharedTrack = {
    'id': 'cloud-song',
    'version': 'v1',
    'customTitle': 'Mây trên phố',
    'provider': 'soundcloud',
    'providerContentId': 'artist/track',
    'canonicalUrl': 'https://soundcloud.com/artist/track',
    'originalUrl': 'https://on.soundcloud.com/AbCdEf',
    'originalTitle': 'City Clouds',
    'originalCreator': 'Cloud Artist',
    'audioReady': true,
  };

  @override
  Future<Map<String, dynamic>> request(
    String method,
    String path, {
    Map<String, dynamic>? data,
    Map<String, dynamic>? query,
    String? version,
    Map<String, dynamic>? headers,
  }) async {
    if (path == '/albums/alb-1') {
      return {
        'id': 'alb-1',
        'name': 'Góc Nhạc Acoustic',
        'version': 'v1',
        'songCount': 1,
      };
    }
    if (path == '/albums/alb-1/songs') {
      albumSongProviderFilter = query?['provider'] as String?;
      if (albumSongProviderFilter != null &&
          albumSongProviderFilter != 'soundcloud') {
        return {'items': <dynamic>[], 'total': 0, 'page': 1, 'pageSize': 30};
      }
      if (albumSongProviderFilter == 'soundcloud') {
        return {
          'items': [
            {..._sharedTrack, 'customTitle': 'Cloud track'},
          ],
          'total': 1,
          'page': 1,
          'pageSize': 30,
        };
      }
      return {
        'items': [_sharedTrack],
        'total': 1,
        'page': 1,
        'pageSize': 30,
      };
    }
    if (path == '/albums/alb-1/share') {
      if (method == 'DELETE') shareRevoked = true;
      return {'albumId': 'alb-1', 'shareCode': 'MUSIC234'};
    }
    if (path == '/shared-albums/MUSIC234') {
      return {
        'shareCode': 'MUSIC234',
        'albumName': 'Album được chia sẻ',
        'sharedBy': 'Minh',
        'songCount': 1,
        'songs': [_sharedTrack],
      };
    }
    if (path == '/shared-albums/MUSIC234/import') {
      sharedImported = true;
      return {'id': 'imported', 'name': 'Album được chia sẻ', 'version': 'v1'};
    }
    if (path == '/library/songs/cloud-song') return _sharedTrack;
    if (path == '/users/me') {
      return {
        'id': 'user-v2',
        'email': 'v2@tmusic.test',
        'displayName': 'Minh',
        'emailVerified': true,
        'authProviders': ['password'],
      };
    }

    if (path == '/search') {
      return {
        'albums': {
          'items': [
            {
              'id': 'alb-1',
              'name': 'Góc Nhạc Acoustic',
              'version': 'v1',
              'isFavorite': true,
              'songCount': 1,
            },
          ],
          'total': 1,
          'page': 1,
          'pageSize': 30,
        },
        'songs': {
          'items': [
            {
              'id': 'sng-1',
              'version': 'v1',
              'customTitle': songTitle,
              'customArtist': 'Nghệ sĩ Indie',
              'provider': 'youtube',
              'providerContentId': 'AbCdEfGh999',
              'canonicalUrl': 'https://www.youtube.com/watch?v=AbCdEfGh999',
              'playbackCapability': 'EMBED_SUPPORTED',
              'isFavorite': isFav,
              'albumIds': ['alb-1'],
            },
          ],
          'total': 1,
          'page': 1,
          'pageSize': 30,
        },
      };
    }

    if (path == '/albums' && method == 'GET') {
      return {
        'items': [
          {
            'id': 'alb-1',
            'name': 'Góc Nhạc Acoustic',
            'version': 'v1',
            'isFavorite': true,
            'songCount': 1,
          },
        ],
        'total': 1,
        'page': 1,
        'pageSize': 30,
      };
    }

    if (path == '/library/songs' && method == 'GET') {
      songProviderFilter = query?['provider'] as String?;
      if (songProviderFilter == 'soundcloud') {
        return {
          'items': [_sharedTrack],
          'total': 1,
          'page': 1,
          'pageSize': 30,
        };
      }
      if (multipleSongPages && query?['page'] == 2) {
        return {
          'items': [_sharedTrack],
          'total': 2,
          'page': 2,
          'pageSize': 1,
        };
      }
      return {
        'items': [
          {
            'id': 'sng-1',
            'version': 'v1',
            'customTitle': songTitle,
            'customArtist': 'Nghệ sĩ Indie',
            'provider': 'youtube',
            'providerContentId': 'AbCdEfGh999',
            'canonicalUrl': 'https://www.youtube.com/watch?v=AbCdEfGh999',
            'playbackCapability': 'EMBED_SUPPORTED',
            'isFavorite': isFav,
            'albumIds': ['alb-1'],
          },
        ],
        'total': multipleSongPages ? 2 : 1,
        'page': 1,
        'pageSize': multipleSongPages ? 1 : 30,
      };
    }

    if (path == '/library/songs/sng-1' && method == 'GET') {
      return {
        'id': 'sng-1',
        'version': 'v1',
        'customTitle': songTitle,
        'customArtist': 'Nghệ sĩ Indie',
        'provider': 'youtube',
        'providerContentId': 'AbCdEfGh999',
        'canonicalUrl': 'https://www.youtube.com/watch?v=AbCdEfGh999',
        'originalTitle': 'Original Studio Video',
        'originalCreator': 'Official Channel',
        'playbackCapability': 'EMBED_SUPPORTED',
        'isFavorite': isFav,
        'albumIds': ['alb-1'],
      };
    }

    if (path == '/library/songs/sng-1' && method == 'PATCH') {
      songTitle = data?['customTitle'] as String? ?? songTitle;
      return {
        'id': 'sng-1',
        'version': 'v2',
        'customTitle': songTitle,
        'customArtist': data?['customArtist'],
        'provider': 'youtube',
        'providerContentId': 'AbCdEfGh999',
        'canonicalUrl': 'https://www.youtube.com/watch?v=AbCdEfGh999',
        'playbackCapability': 'EMBED_SUPPORTED',
        'isFavorite': isFav,
        'albumIds': ['alb-1'],
      };
    }

    if (path == '/library/songs/sng-1/favorite' && method == 'PATCH') {
      if (failFavorite) throw const ApiFailure('network_error');
      isFav = data?['isFavorite'] == true;
      return {
        'id': 'sng-1',
        'version': 'v2',
        'customTitle': songTitle,
        'provider': 'youtube',
        'providerContentId': 'AbCdEfGh999',
        'canonicalUrl': 'https://www.youtube.com/watch?v=AbCdEfGh999',
        'playbackCapability': 'EMBED_SUPPORTED',
        'isFavorite': isFav,
        'albumIds': ['alb-1'],
      };
    }

    if (path == '/downloads' && method == 'GET') {
      return {
        'items': [
          {
            'id': 'job-1',
            'songId': 'sng-1',
            'songTitle': 'Bản Acoustic Đêm',
            'songArtist': 'Nghệ Sĩ Độc Lập',
            'provider': 'youtube',
            'canonicalUrl': 'https://www.youtube.com/watch?v=AbCdEfGh999',
            'status': 'COMPLETE',
            'progressPercent': 100,
            'audioReady': true,
            'audioStreamUrl': '/api/v1/library/songs/sng-1/audio',
          },
        ],
        'total': 1,
        'page': 1,
        'pageSize': 30,
      };
    }

    return {'items': <dynamic>[], 'total': 0, 'page': 1, 'pageSize': 30};
  }
}

void main() {
  testWidgets('downloaded filter finds device MP3s beyond the first page', (
    tester,
  ) async {
    final api = _ScenarioApi()..multipleSongPages = true;
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          sessionProvider.overrideWith(_ActiveSession.new),
          publicApiProvider.overrideWithValue(api),
          authorizedApiProvider.overrideWithValue(api),
          localLibraryProvider.overrideWith((_) async => _DownloadedLibrary()),
        ],
        child: const TMusicApp(initialLocation: '/albums'),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(ChoiceChip, 'Đã tải MP3'));
    await tester.pumpAndSettle();
    expect(find.text('Mây trên phố'), findsOneWidget);
    expect(find.text('Bản Acoustic Đêm'), findsNothing);
    expect(find.byIcon(Icons.arrow_circle_down_rounded), findsOneWidget);
  });

  testWidgets('library filters SoundCloud and opens its attributed song', (
    tester,
  ) async {
    final api = _ScenarioApi();
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          sessionProvider.overrideWith(_ActiveSession.new),
          publicApiProvider.overrideWithValue(api),
          authorizedApiProvider.overrideWithValue(api),
        ],
        child: const TMusicApp(initialLocation: '/albums'),
      ),
    );
    await tester.pumpAndSettle();
    final soundcloud = find.text('SoundCloud');
    await tester.ensureVisible(soundcloud);
    await tester.pumpAndSettle();
    await tester.tap(soundcloud);
    await tester.pumpAndSettle();
    expect(api.songProviderFilter, 'soundcloud');
    expect(find.text('Nguồn: SoundCloud • Cloud Artist'), findsOneWidget);
    await tester.tap(find.text('Mây trên phố'));
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(find.text('Mở trong SoundCloud'), 150);
    expect(find.text('City Clouds'), findsOneWidget);
    expect(find.text('https://on.soundcloud.com/AbCdEf'), findsOneWidget);
  });

  testWidgets('album creates and revokes a share code', (tester) async {
    final api = _ScenarioApi();
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          sessionProvider.overrideWith(_ActiveSession.new),
          publicApiProvider.overrideWithValue(api),
          authorizedApiProvider.overrideWithValue(api),
        ],
        child: const TMusicApp(initialLocation: '/albums/alb-1'),
      ),
    );
    await tester.pumpAndSettle();
    final play = find.byKey(const Key('album-play-mp3'));
    expect(tester.getSize(play), const Size.square(56));
    await tester.tap(find.byKey(const Key('album-share')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('album-share-code-text')), findsOneWidget);
    await tester.tap(find.text('Thu hồi mã chia sẻ'));
    await tester.pumpAndSettle();
    expect(api.shareRevoked, isTrue);
    expect(find.byKey(const Key('album-share-code-text')), findsNothing);
  });

  testWidgets('album source filter scopes its songs request', (tester) async {
    final api = _ScenarioApi();
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          sessionProvider.overrideWith(_ActiveSession.new),
          publicApiProvider.overrideWithValue(api),
          authorizedApiProvider.overrideWithValue(api),
        ],
        child: const TMusicApp(initialLocation: '/albums/alb-1'),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Mây trên phố'), findsOneWidget);

    await tester.tap(find.byKey(const Key('album-source-soundcloud')));
    await tester.pumpAndSettle();

    expect(api.albumSongProviderFilter, 'soundcloud');
    expect(find.text('Cloud track'), findsOneWidget);
    expect(find.text('Mây trên phố'), findsNothing);
  });

  testWidgets('album local source filter displays device audio only', (
    tester,
  ) async {
    final api = _ScenarioApi();
    final local = _DownloadedLibrary()
      ..albumTracks = const [
        LocalTrack('import-1', 'Bản thu trên máy', '/device/import-1.mp3'),
      ];
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          sessionProvider.overrideWith(_ActiveSession.new),
          publicApiProvider.overrideWithValue(api),
          authorizedApiProvider.overrideWithValue(api),
          localLibraryProvider.overrideWith((_) async => local),
          localAudioProvider.overrideWith((_) async => LocalAudio.forTest()),
        ],
        child: const TMusicApp(initialLocation: '/albums/alb-1'),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Bản thu trên máy'), findsOneWidget);

    await tester.tap(find.byKey(const Key('album-source-local')));
    await tester.pumpAndSettle();

    expect(api.albumSongProviderFilter, isNull);
    expect(find.text('Bản thu trên máy'), findsOneWidget);
    expect(find.text('Mây trên phố'), findsNothing);
  });

  testWidgets('local audio can be assigned to an existing album', (
    tester,
  ) async {
    final api = _ScenarioApi();
    final store = GuestLibraryStore();
    final album = await store.saveAlbum(
      name: 'Góc Nhạc Acoustic',
      description: '',
    );
    final local = _DownloadedLibrary()
      ..tracks = const [
        LocalTrack('import-1', 'Bản thu trên máy', '/device/import-1.mp3'),
      ];
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          sessionProvider.overrideWith(_GuestSession.new),
          guestLibraryStoreProvider.overrideWithValue(store),
          publicApiProvider.overrideWithValue(api),
          authorizedApiProvider.overrideWithValue(api),
          localLibraryProvider.overrideWith((_) async => local),
          localAudioProvider.overrideWith((_) async => LocalAudio.forTest()),
        ],
        child: const MaterialApp(
          locale: Locale('vi'),
          home: LocalAudioScreen(),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('local-track-add-album-import-1')));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 350));
    expect(find.text('Góc Nhạc Acoustic'), findsOneWidget);
    await tester.tap(find.text('Góc Nhạc Acoustic'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 350));

    expect(local.addedTrackId, 'import-1');
    expect(local.addedAlbumId, album.id);
  });

  testWidgets('library previews and imports an attributed shared album', (
    tester,
  ) async {
    final api = _ScenarioApi();
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          sessionProvider.overrideWith(_ActiveSession.new),
          publicApiProvider.overrideWithValue(api),
          authorizedApiProvider.overrideWithValue(api),
        ],
        child: const TMusicApp(initialLocation: '/albums'),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('library-import-shared-album')));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('enter-share-code-input')),
      'music234',
    );
    await tester.tap(find.byKey(const Key('preview-shared-album-button')));
    await tester.pumpAndSettle();
    expect(find.text('Nguồn: SoundCloud • Cloud Artist'), findsOneWidget);
    final submit = find.byKey(const Key('import-shared-album-submit'));
    await tester.ensureVisible(submit);
    await tester.tap(submit);
    await tester.pumpAndSettle();
    expect(api.sharedImported, isTrue);
  });

  testWidgets('guest sync keeps local album when a cloud request fails', (
    tester,
  ) async {
    final api = _ScenarioApi(failFavorite: true);
    final store = GuestLibraryStore();
    final album = await store.saveAlbum(name: 'Nhạc cục bộ', description: '');
    await store.saveSong(
      url: 'https://soundcloud.com/artist/track',
      provider: 'soundcloud',
      providerContentId: 'artist/track',
      title: 'Nhạc trên máy',
      albumIds: [album.id],
    );
    final container = ProviderContainer(
      overrides: [
        sessionProvider.overrideWith(_ActiveSession.new),
        authorizedApiProvider.overrideWithValue(api),
        guestLibraryStoreProvider.overrideWithValue(store),
      ],
    );
    addTearDown(container.dispose);
    await container.read(sessionProvider.future);
    final repo = container.read(libraryRepositoryProvider);
    expect(await repo.syncGuestDataToCloud(userId: 'user-v2'), isFalse);
    expect((await store.getAlbum(album.id)).songCount, 1);
  });
  testWidgets('unified SearchScreen displays both matching albums and songs', (
    tester,
  ) async {
    FlutterSecureStorage.setMockInitialValues({});
    await tester.binding.setSurfaceSize(const Size(412, 892));
    addTearDown(() => tester.binding.setSurfaceSize(null));

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          sessionProvider.overrideWith(_ActiveSession.new),
          authorizedApiProvider.overrideWithValue(_ScenarioApi()),
          settingsProvider.overrideWith(_DefaultSettings.new),
        ],
        child: const TMusicApp(initialLocation: '/search'),
      ),
    );
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byKey(const Key('global-search-input')),
      'Acoustic',
    );
    await tester.pump(const Duration(milliseconds: 400));
    await tester.pumpAndSettle();

    expect(find.text('Góc Nhạc Acoustic'), findsOneWidget);
    expect(find.text('Bản Acoustic Đêm'), findsOneWidget);
  });

  testWidgets(
    'FavoritesScreen shows favorite albums and favorite songs together',
    (tester) async {
      FlutterSecureStorage.setMockInitialValues({});
      await tester.binding.setSurfaceSize(const Size(412, 892));
      addTearDown(() => tester.binding.setSurfaceSize(null));

      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            sessionProvider.overrideWith(_ActiveSession.new),
            authorizedApiProvider.overrideWithValue(_ScenarioApi()),
            settingsProvider.overrideWith(_DefaultSettings.new),
          ],
          child: const TMusicApp(initialLocation: '/favorites'),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Album yêu thích'), findsOneWidget);
      expect(find.text('Bài hát yêu thích'), findsOneWidget);
      expect(find.text('Góc Nhạc Acoustic'), findsOneWidget);
      expect(find.text('Bản Acoustic Đêm'), findsOneWidget);
    },
  );

  testWidgets(
    'SongScreen removes embedded WebView player, keeps source fallback, album chip, and rolls back failed favorite toggle',
    (tester) async {
      FlutterSecureStorage.setMockInitialValues({});
      await tester.binding.setSurfaceSize(const Size(412, 950));
      addTearDown(() => tester.binding.setSurfaceSize(null));

      final api = _ScenarioApi(failFavorite: true);
      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            sessionProvider.overrideWith(_ActiveSession.new),
            authorizedApiProvider.overrideWithValue(api),
            settingsProvider.overrideWith(_DefaultSettings.new),
          ],
          child: const TMusicApp(initialLocation: '/songs/sng-1'),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('toggle-embed-player')), findsNothing);
      expect(find.text('Mở trong YouTube'), findsOneWidget);

      // Tap favorite remove -> optimistic update then rollback on API error
      await tester.tap(find.text('Bỏ yêu thích'));
      await tester.pumpAndSettle();
      expect(find.text('Bỏ yêu thích'), findsOneWidget);
      expect(
        find.text('Không kết nối được. Kiểm tra mạng rồi thử lại.'),
        findsOneWidget,
      );

      await tester.scrollUntilVisible(find.text('Góc Nhạc Acoustic'), 150);
      expect(find.text('Góc Nhạc Acoustic'), findsOneWidget);
    },
  );

  testWidgets('EditSongScreen updates custom title', (tester) async {
    FlutterSecureStorage.setMockInitialValues({});
    await tester.binding.setSurfaceSize(const Size(412, 892));
    addTearDown(() => tester.binding.setSurfaceSize(null));

    final api = _ScenarioApi();
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          sessionProvider.overrideWith(_ActiveSession.new),
          authorizedApiProvider.overrideWithValue(api),
          settingsProvider.overrideWith(_DefaultSettings.new),
        ],
        child: const TMusicApp(initialLocation: '/songs/sng-1/edit'),
      ),
    );
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byKey(const Key('edit-song-title')),
      'Tên Đã Chỉnh Sửa',
    );
    await tester.tap(find.byKey(const Key('edit-song-save')));
    await tester.pumpAndSettle();
    expect(api.songTitle, 'Tên Đã Chỉnh Sửa');
  });

  testWidgets(
    'DeleteAccountScreen requires confirmation checkbox before enabling submit',
    (tester) async {
      FlutterSecureStorage.setMockInitialValues({});
      await tester.binding.setSurfaceSize(const Size(412, 892));
      addTearDown(() => tester.binding.setSurfaceSize(null));

      final api = _ScenarioApi();
      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            sessionProvider.overrideWith(_ActiveSession.new),
            authorizedApiProvider.overrideWithValue(api),
            settingsProvider.overrideWith(_DefaultSettings.new),
          ],
          child: const TMusicApp(initialLocation: '/account/delete'),
        ),
      );
      await tester.pumpAndSettle();

      final submitBefore = tester.widget<FilledButton>(
        find.byKey(const Key('delete-account-submit')),
      );
      expect(submitBefore.onPressed, isNull);

      await tester.tap(find.byKey(const Key('delete-account-confirm')));
      await tester.pumpAndSettle();

      final submitAfter = tester.widget<FilledButton>(
        find.byKey(const Key('delete-account-submit')),
      );
      expect(submitAfter.onPressed, isNotNull);
    },
  );

  testWidgets('DownloadsScreen displays completed MP3 download job', (
    tester,
  ) async {
    FlutterSecureStorage.setMockInitialValues({});
    await tester.binding.setSurfaceSize(const Size(412, 892));
    addTearDown(() => tester.binding.setSurfaceSize(null));

    final api = _ScenarioApi();
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          sessionProvider.overrideWith(_ActiveSession.new),
          authorizedApiProvider.overrideWithValue(api),
          settingsProvider.overrideWith(_DefaultSettings.new),
        ],
        child: const TMusicApp(initialLocation: '/downloads'),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Tiến trình tải MP3'), findsOneWidget);
    expect(find.text('Bản Acoustic Đêm'), findsOneWidget);
    expect(find.text('Đã có MP3 riêng'), findsOneWidget);
  });

  testWidgets(
    'Offline cold-start restores saved userId session instead of locking on loading screen',
    (tester) async {
      FlutterSecureStorage.setMockInitialValues({
        SessionController.storageKey: '{"refreshToken":"offline-refresh-token","userId":"offline-user-1"}',
      });
      await tester.binding.setSurfaceSize(const Size(412, 892));
      addTearDown(() => tester.binding.setSurfaceSize(null));

      final offlineApi = _ScenarioApi();
      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            publicApiProvider.overrideWithValue(_OfflinePublicApi()),
            authorizedApiProvider.overrideWithValue(offlineApi),
            settingsProvider.overrideWith(_DefaultSettings.new),
          ],
          child: const TMusicApp(),
        ),
      );
      await tester.pumpAndSettle();

      // Reaches HomeScreen with OfflineBanner instead of getting stuck on SessionLoadingScreen!
      expect(find.byKey(const Key('home-add')), findsOneWidget);
      expect(find.byKey(const Key('nav-downloads')), findsOneWidget);
    },
  );

  testWidgets(
    'MiniPlayerDock opens NowPlayingSheet with seek timestamps, 3-state repeat, A-B segment loop, speed, and queue without 320dp overflow',
    (tester) async {
      FlutterSecureStorage.setMockInitialValues({});
      await tester.binding.setSurfaceSize(const Size(320, 820));
      addTearDown(() => tester.binding.setSurfaceSize(null));

      final player = LocalAudio.forTest(
        initialPosition: const Duration(seconds: 70),
        initialDuration: const Duration(seconds: 225),
        initialQueue: const [
          MediaItem(
            id: 'song:sng-1',
            title: 'Bản Remix Đêm Khuya',
            artist: 'DJ TMusic',
            album: 'nhac dui',
          ),
          MediaItem(
            id: 'song:sng-2',
            title: 'Giai Điệu Bình Minh',
            artist: 'Indie Band',
            album: 'nhac dui',
          ),
        ],
      );

      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            sessionProvider.overrideWith(_ActiveSession.new),
            authorizedApiProvider.overrideWithValue(_ScenarioApi()),
            settingsProvider.overrideWith(_DefaultSettings.new),
            localAudioProvider.overrideWith((_) async => player),
          ],
          child: const TMusicApp(),
        ),
      );
      await tester.pumpAndSettle();

      // MiniPlayerDock visible at bottom of shell
      expect(find.byKey(const Key('mini-player-dock')), findsOneWidget);
      expect(find.text('Bản Remix Đêm Khuya'), findsOneWidget);

      // Tap MiniPlayerDock -> opens full NowPlayingSheet
      await tester.tap(find.byKey(const Key('mini-player-dock')));
      await tester.pumpAndSettle();

      // Verify formatted timestamps (1:10 / 3:45)
      expect(find.text('1:10'), findsOneWidget);
      expect(find.text('3:45'), findsOneWidget);

      // Forward +10s -> 1:20
      await tester.tap(find.byKey(const Key('player-forward-10')));
      await tester.pumpAndSettle();
      expect(find.text('1:20'), findsOneWidget);

      // Cycle 3-state repeat: Tắt lặp -> Lặp 1 bài -> Lặp danh sách -> Tắt lặp
      expect(find.text('Tắt lặp'), findsOneWidget);
      await tester.tap(find.byKey(const Key('player-repeat-cycle')));
      await tester.pumpAndSettle();
      expect(player.playbackState.value.repeatMode, AudioServiceRepeatMode.one);
      expect(find.text('Lặp 1 bài'), findsAtLeastNWidgets(1));

      await tester.tap(find.byKey(const Key('player-repeat-cycle')));
      await tester.pumpAndSettle();
      expect(player.playbackState.value.repeatMode, AudioServiceRepeatMode.all);
      expect(find.text('Lặp danh sách'), findsAtLeastNWidgets(1));

      await tester.tap(find.byKey(const Key('player-repeat-cycle')));
      await tester.pumpAndSettle();
      expect(
        player.playbackState.value.repeatMode,
        AudioServiceRepeatMode.none,
      );

      // Open A-B Segment Loop panel -> set point A at 1:20, advance +10s, set point B at 1:30
      await tester.ensureVisible(find.byKey(const Key('player-ab-toggle')));
      await tester.tap(find.byKey(const Key('player-ab-toggle')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('ab-loop-panel')), findsOneWidget);
      expect(find.byKey(const Key('ab-range-slider')), findsOneWidget);

      await tester.ensureVisible(find.byKey(const Key('ab-set-a')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('ab-set-a')));
      await tester.pumpAndSettle();
      expect(player.abLoop.value?.start, const Duration(seconds: 80));

      await player.seek(const Duration(seconds: 110));
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.byKey(const Key('ab-set-b')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('ab-set-b')));
      await tester.pumpAndSettle();
      expect(player.abLoop.value?.start, const Duration(seconds: 80));
      expect(player.abLoop.value?.end, const Duration(seconds: 110));

      // Seeking to/past B (115s) automatically loops back to A (80s = 1:20)!
      await player.seek(const Duration(seconds: 115));
      await tester.pumpAndSettle();
      expect(player.currentPosition, const Duration(seconds: 80));

      // Clear A-B loop
      await tester.ensureVisible(find.byKey(const Key('ab-clear')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('ab-clear')));
      await tester.pumpAndSettle();
      expect(player.abLoop.value, isNull);

      // Playback speed chip -> 1.25x
      await tester.ensureVisible(find.byKey(const Key('speed-1.25')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('speed-1.25')));
      await tester.pumpAndSettle();
      expect(player.speed.value, 1.25);

      // Queue toggle -> jump to track 2
      await tester.tap(find.byKey(const Key('now-playing-queue-toggle')));
      await tester.pumpAndSettle();
      expect(find.text('Giai Điệu Bình Minh'), findsOneWidget);
      await tester.tap(find.text('Giai Điệu Bình Minh'));
      await tester.pumpAndSettle();
      expect(player.mediaItem.value?.id, 'song:sng-2');

      expect(tester.takeException(), isNull);
    },
  );
}

class _OfflinePublicApi extends ApiClient {
  @override
  Future<Map<String, dynamic>> request(
    String method,
    String path, {
    Map<String, dynamic>? data,
    Map<String, dynamic>? query,
    String? version,
    Map<String, dynamic>? headers,
  }) async {
    throw const ApiFailure('network_error');
  }
}
