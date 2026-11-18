import 'dart:async';
import 'dart:ui' show CheckedState;

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:tmusic/core/api/api_client.dart';
import 'package:tmusic/core/storage/guest_library_store.dart';
import 'package:tmusic/core/storage/library_cache.dart';
import 'package:tmusic/features/albums/album_screens.dart';
import 'package:tmusic/features/library/library_repository.dart';
import 'package:tmusic/shared/models/library_models.dart';

// This is the network boundary only: the picker uses the real repository,
// including its album If-Match version and guest membership update behavior.
class _PickerApi extends ApiClient {
  int revision = 1;
  final added = <String>[];
  final versions = <String?>[];
  Completer<void>? firstAdd;
  final failOnce = <String>{};
  final members = <String>{};
  bool paginate = false;
  bool failPageTwoOnce = false;
  final requestedPages = <int>[];

  Map<String, dynamic> _song(String id) => {
    'id': id,
    'version': 'song-$revision',
    'customTitle': switch (id) {
      'song-a' => 'Bài hát A',
      'song-b' => 'Bài hát B',
      _ => 'Bài hát C',
    },
    'provider': 'soundcloud',
    'providerContentId': 'artist/$id',
    'canonicalUrl': 'https://soundcloud.com/artist/$id',
    'originalUrl': 'https://soundcloud.com/artist/$id',
    'originalCreator': 'Nghệ sĩ của bản nhạc',
    'playbackCapability': 'EXTERNAL_ONLY',
    'albumIds': members.contains(id) ? ['album-1'] : <String>[],
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
    if (method == 'GET' && path == '/library/songs') {
      final page = query!['page'] as int;
      requestedPages.add(page);
      if (page == 2 && failPageTwoOnce) {
        failPageTwoOnce = false;
        throw const ApiFailure('unavailable', status: 503);
      }
      return {
        'items': page == 1
            ? [_song('song-a'), _song('song-b')]
            : [_song('song-c')],
        'total': paginate ? 3 : 2,
        'page': page,
        'pageSize': paginate ? 2 : 30,
      };
    }
    if (method == 'GET' && path.startsWith('/library/songs/')) {
      return _song(path.split('/').last);
    }
    if (method == 'GET' && path == '/albums/album-1') {
      return {
        'id': 'album-1',
        'name': 'Nhạc yêu thích',
        'version': 'album-$revision',
        'songCount': members.length,
      };
    }
    if (method == 'POST' && path == '/albums/album-1/songs') {
      final id = data!['userLibrarySongId'] as String;
      versions.add(version);
      if (version != 'album-$revision') {
        throw const ApiFailure('conflict', status: 412);
      }
      if (id == 'song-a') await firstAdd?.future;
      if (failOnce.remove(id)) {
        throw const ApiFailure('unavailable', status: 503);
      }
      members.add(id);
      added.add(id);
      revision++;
      return {};
    }
    throw StateError('Unexpected test request: $method $path');
  }
}

Future<void> _pumpPicker(
  WidgetTester tester,
  _PickerApi api, {
  Size size = const Size(412, 920),
  double textScale = 1,
  Set<String> excludeIds = const {},
  GuestLibraryStore? guestStore,
  Album album = const Album(
    id: 'album-1',
    name: 'Nhạc yêu thích',
    version: 'stale-on-purpose',
  ),
}) async {
  await tester.binding.setSurfaceSize(size);
  addTearDown(() => tester.binding.setSurfaceSize(null));
  addTearDown(api.close);
  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        libraryRepositoryProvider.overrideWith(
          (ref) => LibraryRepository(
            api: api,
            publicApi: api,
            cache: LibraryCache('picker-test'),
            guestStore: guestStore ?? GuestLibraryStore(),
            offline: ref.read(offlineModeProvider.notifier),
            isGuest: guestStore != null,
            currentUserId: () => 'picker-test',
          ),
        ),
      ],
      child: MaterialApp(
        locale: const Locale('vi'),
        supportedLocales: const [Locale('vi'), Locale('en')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        theme: ThemeData.dark(useMaterial3: true),
        builder: (context, child) => MediaQuery(
          data: MediaQuery.of(context)
              .copyWith(textScaler: TextScaler.linear(textScale)),
          child: child!,
        ),
        home: Scaffold(
          body: ExistingSongPicker(album: album, excludeIds: excludeIds),
        ),
      ),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('songs have explicit selectors and wait for Add confirmation', (
    tester,
  ) async {
    final api = _PickerApi();
    await _pumpPicker(tester, api);

    // Removing the selector or reintroducing tap-to-close breaks this contract.
    expect(find.byType(Checkbox), findsNWidgets(2));
    final confirm = find.byKey(const Key('album-picker-add'));
    expect(tester.widget<FilledButton>(confirm).onPressed, isNull);
    await tester.tap(find.byKey(const Key('album-picker-select-song-a')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('album-picker-row-song-b')));
    await tester.pump();

    expect(api.added, isEmpty);
    expect(find.text('Đã chọn: 2'), findsOneWidget);
    expect(tester.widget<FilledButton>(confirm).onPressed, isNotNull);
    expect(
      find.text('Nguồn: SoundCloud • Nghệ sĩ của bản nhạc'),
      findsNWidgets(2),
    );
    final semantics = tester.ensureSemantics();
    try {
      await tester.pump();
      final checked = tester
          .getSemantics(find.byKey(const Key('album-picker-select-song-a')))
          .getSemanticsData();
      expect(checked.flagsCollection.isChecked, CheckedState.isTrue);
      expect(checked.label, contains('Bài hát A'));
    } finally {
      semantics.dispose();
    }
  });

  testWidgets('batch addition waits and refreshes the album version per song', (
    tester,
  ) async {
    final api = _PickerApi()..firstAdd = Completer<void>();
    await _pumpPicker(tester, api);
    await tester.tap(find.byKey(const Key('album-picker-select-song-a')));
    await tester.tap(find.byKey(const Key('album-picker-select-song-b')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('album-picker-add')));
    await tester.pump();

    expect(find.byKey(const Key('album-picker-busy-song-a')), findsOneWidget);
    expect(api.versions, ['album-1']);
    expect(
      tester
          .widget<Checkbox>(find.byKey(const Key('album-picker-select-song-b')))
          .onChanged,
      isNull,
    );
    api.firstAdd!.complete();
    await tester.pumpAndSettle();

    expect(api.added, ['song-a', 'song-b']);
    expect(api.versions, ['album-1', 'album-2']);
    expect(find.text('Đã thêm vào album'), findsNWidgets(2));
    expect(find.text('Đã chọn: 0'), findsOneWidget);
    expect(
      tester
          .widget<FilledButton>(find.byKey(const Key('album-picker-add')))
          .onPressed,
      isNull,
    );
  });

  testWidgets(
    'partial failures keep selection and error until successful retry',
    (tester) async {
      final api = _PickerApi()..failOnce.add('song-b');
      await _pumpPicker(tester, api);
      await tester.tap(find.byKey(const Key('album-picker-select-song-a')));
      await tester.tap(find.byKey(const Key('album-picker-select-song-b')));
      await tester.pump();
      await tester.tap(find.byKey(const Key('album-picker-add')));
      await tester.pumpAndSettle();

      expect(api.added, ['song-a']);
      expect(
        find.byKey(const Key('album-picker-error-song-b')),
        findsOneWidget,
      );
      expect(find.text('Đã chọn: 1'), findsOneWidget);
      expect(
        tester
            .widget<Checkbox>(
              find.byKey(const Key('album-picker-select-song-b')),
            )
            .value,
        isTrue,
      );
      await tester.tap(find.byKey(const Key('album-picker-select-song-b')));
      await tester.pump();
      expect(
        find.byKey(const Key('album-picker-error-song-b')),
        findsOneWidget,
      );
      await tester.tap(find.byKey(const Key('album-picker-select-song-b')));
      await tester.pump();
      await tester.tap(find.byKey(const Key('album-picker-add')));
      await tester.pumpAndSettle();

      expect(api.added, ['song-a', 'song-b']);
      expect(api.versions, ['album-1', 'album-2', 'album-2']);
      expect(find.byKey(const Key('album-picker-error-song-b')), findsNothing);
      expect(find.text('Đã thêm vào album'), findsNWidgets(2));
    },
  );

  testWidgets('membership beyond the loaded album page cannot be added twice', (
    tester,
  ) async {
    final api = _PickerApi()..members.add('song-b');
    await _pumpPicker(tester, api);

    expect(find.text('Đã có trong album'), findsOneWidget);
    final existing = tester.widget<Checkbox>(
      find.byKey(const Key('album-picker-select-song-b')),
    );
    expect(existing.value, isTrue);
    expect(existing.onChanged, isNull);
    await tester.tap(find.byKey(const Key('album-picker-row-song-b')));
    await tester.pump();
    expect(find.text('Đã chọn: 0'), findsOneWidget);
    expect(api.added, isEmpty);
  });

  testWidgets('320dp at 200 percent keeps selection and Add controls usable', (
    tester,
  ) async {
    final api = _PickerApi();
    await _pumpPicker(tester, api, size: const Size(320, 740), textScale: 2);
    expect(tester.takeException(), isNull);
    final selector = find.byKey(const Key('album-picker-select-song-a'));
    await tester.scrollUntilVisible(selector, 120);
    await tester.pumpAndSettle();
    expect(tester.getSize(selector).width, greaterThanOrEqualTo(48));
    expect(tester.getSize(selector).height, greaterThanOrEqualTo(48));
    await tester.tap(selector);
    await tester.pump();
    expect(find.text('Đã chọn: 1'), findsOneWidget);
    await tester.tap(find.byKey(const Key('album-picker-add')));
    await tester.pumpAndSettle();
    expect(api.added, ['song-a']);
    expect(tester.takeException(), isNull);
  });

  testWidgets('a failed next page retries the same page and keeps selection', (
    tester,
  ) async {
    final api = _PickerApi()
      ..paginate = true
      ..failPageTwoOnce = true;
    await _pumpPicker(tester, api);
    await tester.tap(find.byKey(const Key('album-picker-select-song-a')));
    await tester.pump();
    await tester.scrollUntilVisible(find.text('Xem thêm'), 160);
    await tester.tap(find.text('Xem thêm'));
    await tester.pumpAndSettle();
    await tester.drag(find.byType(ListView), const Offset(0, 600));
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(find.text('Thử lại'), -120);
    await tester.tap(find.text('Thử lại'));
    await tester.pumpAndSettle();

    expect(api.requestedPages, [1, 2, 2]);
    expect(find.text('Đã chọn: 1'), findsOneWidget);
    await tester.scrollUntilVisible(
      find.byKey(const Key('album-picker-select-song-c')),
      180,
    );
    await tester.tap(find.byKey(const Key('album-picker-select-song-c')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('album-picker-add')));
    await tester.pumpAndSettle();
    expect(api.added, ['song-a', 'song-c']);
  });

  testWidgets(
    'guest selection preserves memberships changed after picker load',
    (tester) async {
      final store = GuestLibraryStore();
      final target = await store.saveAlbum(name: 'Album đích', description: '');
      final other = await store.saveAlbum(name: 'Album khác', description: '');
      final song = await store.saveSong(
        url: 'https://soundcloud.com/artist/guest-track',
        provider: 'soundcloud',
        providerContentId: 'artist/guest-track',
        title: 'Bản nhạc trong máy',
        albumIds: const [],
      );
      final api = _PickerApi();
      await _pumpPicker(tester, api, guestStore: store, album: target);
      // Another local action updated membership after the picker loaded its row.
      await store.updateSong(song.id, albumIds: [other.id]);
      await tester.tap(find.byKey(Key('album-picker-select-${song.id}')));
      await tester.pump();
      await tester.tap(find.byKey(const Key('album-picker-add')));
      await tester.pumpAndSettle();

      expect(
        (await store.getSong(song.id)).albumIds,
        containsAll([target.id, other.id]),
      );
      expect((await store.getAlbum(target.id)).songCount, 1);
      expect(api.versions, isEmpty);
      expect(find.text('Đã thêm vào album'), findsOneWidget);
    },
  );
}
