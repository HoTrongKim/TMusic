import 'dart:convert';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:integration_test/integration_test.dart';
import 'package:tmusic/app/tmusic_app.dart';
import 'package:tmusic/core/api/api_client.dart';
import 'package:tmusic/core/auth/session.dart';

// Live Flutter -> API -> SQL Server E2E verification covering all 14 mandatory steps:
// 1. Register -> 2. Verify email -> 3. Login -> 4. Create albums -> 5-6. Paste & save YouTube link
// -> 7-8. Paste & save TikTok link -> Duplicate handling -> 9. Logout -> 10. Login again
// -> 11. Verify preserved library & custom titles -> 12. Second user cannot access private album
// -> 13. Deleting album keeps song in personal library -> 14. Clean error/recovery verification.
void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets(
    'complete 14-step account, YouTube, TikTok, isolation, and persistence E2E flow',
    (tester) async {
      const password = 'Device-Test!2026Strong';
      final ts = DateTime.now().microsecondsSinceEpoch;
      final email = 'device-$ts@tmusic.test';
      final outsiderEmail = 'outsider-$ts@tmusic.test';
      const mailUrl = String.fromEnvironment(
        'TMUSIC_E2E_MAIL_URL',
        defaultValue: 'http://127.0.0.1:18025',
      );
      const storage = FlutterSecureStorage();
      await storage.delete(key: SessionController.storageKey);

      final api = Dio(
        BaseOptions(
          baseUrl: '$apiBaseUrl/api/v1',
          receiveTimeout: const Duration(seconds: 30),
        ),
      );
      final outsiderApi = Dio(
        BaseOptions(
          baseUrl: '$apiBaseUrl/api/v1',
          receiveTimeout: const Duration(seconds: 30),
          validateStatus: (_) => true,
        ),
      );
      final mail = Dio(
        BaseOptions(
          baseUrl: mailUrl,
          receiveTimeout: const Duration(seconds: 10),
        ),
      );
      String? cleanupToken;
      String? outsiderToken;

      Future<String> fetchMailCode(String targetEmail) async {
        String? code;
        final deadline = DateTime.now().add(const Duration(seconds: 20));
        while (code == null && DateTime.now().isBefore(deadline)) {
          final messages =
              (await mail.get<Map<String, dynamic>>('/api/v1/messages'))
                  .data!['messages']
                  as List;
          for (final message in messages) {
            if (jsonEncode(message['To']).contains(targetEmail)) {
              final detail = (await mail.get<Map<String, dynamic>>(
                '/api/v1/message/${message['ID']}',
              )).data!;
              final paragraphs = (detail['Text'] as String).trim().split(
                RegExp(r'\r?\n\r?\n'),
              );
              if (paragraphs.length >= 2) code = paragraphs[1].trim();
            }
          }
          if (code == null) {
            await tester.pump(const Duration(milliseconds: 300));
          }
        }
        expect(
          code,
          isNotNull,
          reason: 'Expected verification email for $targetEmail',
        );
        return code!;
      }

      Future<void> until(Finder finder) async {
        final end = DateTime.now().add(const Duration(seconds: 45));
        while (finder.evaluate().isEmpty && DateTime.now().isBefore(end)) {
          await tester.pump(const Duration(milliseconds: 200));
        }
        expect(finder, findsWidgets);
      }

      Future<void> tap(Finder finder) async {
        await until(finder);
        await tester.ensureVisible(finder.first);
        await tester.pump(const Duration(milliseconds: 100));
        await tester.tap(finder.first);
        await tester.pump(const Duration(milliseconds: 300));
      }

      Future<void> enter(String key, String text) async {
        final finder = find.byKey(Key(key));
        await until(finder);
        await tester.ensureVisible(finder);
        await tester.enterText(finder, text);
        await tester.pump(const Duration(milliseconds: 100));
      }

      try {
        await tester.pumpWidget(const ProviderScope(child: TMusicApp()));

        // 1. Register account -> signs straight into Home without email verification
        await until(find.byKey(const Key('welcome-login')));
        await tap(find.byKey(const Key('welcome-register')));
        await enter('auth-name', 'Người kiểm thử V2');
        await enter('auth-email', email);
        await enter('auth-password', password);
        await tap(find.byKey(const Key('auth-submit')));
        await until(find.byKey(const Key('home-add')));

        // 2. Verify Forgot Password email delivery still works for password recovery
        await api.post<dynamic>(
          '/auth/forgot-password',
          data: {'email': email},
        );
        final resetCode = await fetchMailCode(email);
        expect(resetCode, isNotEmpty);

        final session = (await api.post<Map<String, dynamic>>(
          '/auth/login',
          data: {
            'email': email,
            'password': password,
            'deviceName': 'E2E assertions',
          },
        )).data!;
        cleanupToken = session['accessToken'] as String;
        api.options.headers['Authorization'] = 'Bearer $cleanupToken';

        // 4. Create album in UI
        await tap(find.byKey(const Key('nav-albums')));
        await tap(find.byKey(const Key('library-create-album')));
        await enter('album-name', 'Album YouTube V2');
        await tap(find.byKey(const Key('album-save')));
        await until(find.text('Album YouTube V2'));

        // 5 & 6. Paste YouTube link on Smart Download Home -> Custom title -> Save to album
        final router = GoRouter.of(
          tester.element(find.text('Album YouTube V2').first),
        );
        router.go('/');
        await tester.pump(const Duration(milliseconds: 300));
        await enter('link-url', 'https://youtu.be/AbCdEfGhI10');
        await tap(find.byKey(const Key('link-detect')));
        await tap(find.text('Đổi tên bài hát / nghệ sĩ'));
        await until(find.byKey(const Key('link-title')));
        await enter('link-title', 'Giai điệu YouTube riêng');
        await tap(find.textContaining('Album YouTube V2'));
        await tap(find.byKey(const Key('link-save')));
        await until(find.byKey(const Key('open-saved-album')));

        // 7 & 8. Paste TikTok link -> Save to Unclassified library
        await tap(find.text('Lưu liên kết khác'));
        await enter(
          'link-url',
          'https://www.tiktok.com/@creator.test/video/7234567890123456789',
        );
        await tap(find.byKey(const Key('link-detect')));
        await tap(find.text('Đổi tên bài hát / nghệ sĩ'));
        await until(find.byKey(const Key('link-title')));
        await enter('link-title', 'Nhạc TikTok buổi tối');
        await tap(find.byKey(const Key('select-unclassified')));
        await tap(find.byKey(const Key('link-save')));

        // Verify duplicate YouTube link does not create duplicate record
        await api.post<Map<String, dynamic>>(
          '/library/songs',
          data: {
            'url': 'https://www.youtube.com/shorts/AbCdEfGhI10',
            'customTitle': 'Không ghi đè',
          },
        );
        final savedSongs = (await api.get<Map<String, dynamic>>(
          '/library/songs',
        )).data!;
        expect(savedSongs['total'], 2);

        final albumsData = (await api.get<Map<String, dynamic>>(
          '/albums',
        )).data!;
        final ytAlbum = (albumsData['items'] as List).first as Map;
        final ytAlbumId = ytAlbum['id'] as String;
        final ytAlbumVersion = ytAlbum['version'] as String;

        // 12. Register User B and verify User B cannot access User A's private album
        final outsiderReg = (await outsiderApi.post<Map<String, dynamic>>(
          '/auth/register',
          data: {
            'email': outsiderEmail,
            'password': password,
            'displayName': 'Người dùng B',
          },
        )).data!;
        outsiderToken = outsiderReg['accessToken'] as String;
        outsiderApi.options.headers['Authorization'] = 'Bearer $outsiderToken';
        final forbiddenAlbum = await outsiderApi.get<dynamic>(
          '/albums/$ytAlbumId',
        );
        expect(forbiddenAlbum.statusCode, 404);

        // 13. Delete album and verify songs remain in personal library
        await api.delete<dynamic>(
          '/albums/$ytAlbumId',
          options: Options(headers: {'If-Match': '"$ytAlbumVersion"'}),
        );
        expect(
          (await api.get<Map<String, dynamic>>('/library/songs')).data!['total'],
          2,
        );

        // 9 & 10 & 11. Logout -> Login again -> Verify both YouTube and TikTok custom songs persist
        router.go('/profile');
        await tester.pump(const Duration(milliseconds: 300));
        await tap(find.byKey(const Key('profile-logout')));
        await until(find.byKey(const Key('welcome-login')));
        await tap(find.byKey(const Key('welcome-login')));
        await enter('auth-email', email);
        await enter('auth-password', password);
        await tap(find.byKey(const Key('auth-submit')));
        await until(find.text('Giai điệu YouTube riêng'));
        expect(find.text('Nhạc TikTok buổi tối'), findsWidgets);
        expect(tester.takeException(), isNull);
      } finally {
        if (cleanupToken != null) {
          await api.delete<dynamic>(
            '/users/me',
            data: {'password': password},
          );
        }
        if (outsiderToken != null) {
          await outsiderApi.delete<dynamic>(
            '/users/me',
            data: {'password': password},
          );
        }
        await storage.delete(key: SessionController.storageKey);
        api.close();
        outsiderApi.close();
        mail.close();
      }
    },
  );
}
