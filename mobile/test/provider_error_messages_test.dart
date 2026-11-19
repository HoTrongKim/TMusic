import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:tmusic/core/api/api_client.dart';
import 'package:tmusic/core/widgets/common.dart';
import 'package:tmusic/shared/models/library_models.dart';

void main() {
  const messages = {
    'vi': {
      'UNSAFE_SOURCE_REDIRECT': 'Liên kết chuyển tới địa chỉ không được hỗ trợ. Hãy mở trong ứng dụng gốc rồi sao chép lại liên kết trực tiếp của bài đăng.',
      'INVALID_SOURCE_URL': 'Liên kết không hợp lệ hoặc chưa được hỗ trợ. Hãy dùng liên kết HTTPS từ YouTube, TikTok hoặc SoundCloud.',
      'SOURCE_UNAVAILABLE': 'Nội dung không khả dụng. Hãy kiểm tra liên kết trong ứng dụng gốc hoặc chọn nội dung khác.',
      'TIKTOK_IMPORT_DISABLED':
          'Nhận diện liên kết TikTok hiện chưa sẵn sàng. Vui lòng thử lại sau.',
      'SOUNDCLOUD_IMPORT_DISABLED': 'Nhận diện liên kết SoundCloud hiện chưa sẵn sàng. Vui lòng thử lại sau.',
      'SOURCE_ACCESS_CHECK_REQUIRED': 'Nguồn đang yêu cầu xác minh truy cập. Hãy mở nội dung trong ứng dụng gốc; TMusic chưa thể tải nội dung này.',
      'SOURCE_AUTHENTICATION_REQUIRED': 'Nguồn yêu cầu đăng nhập hoặc quyền truy cập. TMusic không thể dùng phiên đăng nhập của bạn để tải nội dung này.',
      'SOURCE_ACCESS_FORBIDDEN': 'Nguồn từ chối yêu cầu tải nội dung này (403). Hãy mở liên kết trong ứng dụng gốc hoặc chọn nguồn được phép tải.',
      'SOURCE_NETWORK_ERROR': 'Kết nối tới nguồn bị gián đoạn. Hãy kiểm tra mạng rồi thử lại sau.',
      'SOURCE_RESTRICTED_OR_UNSUPPORTED': 'Nguồn đánh dấu nội dung là riêng tư, có giới hạn hoặc không hỗ trợ tải. TMusic không thể tải nội dung này.',
      'SOURCE_RESPONSE_UNSUPPORTED': 'Chưa đọc được phản hồi từ nguồn. Hãy thử lại sau hoặc mở nội dung trong ứng dụng gốc.',
      'SOURCE_AUDIO_UNAVAILABLE': 'Bài đăng chưa cung cấp âm thanh công khai có thể tải. Bạn vẫn có thể mở nội dung trong ứng dụng gốc.',
      'RATE_LIMITED': 'Có quá nhiều yêu cầu. Vui lòng chờ một lát rồi thử lại.',
      'PROVIDER_TEMPORARILY_UNAVAILABLE':
          'Nguồn đang tạm thời gián đoạn. Vui lòng thử lại sau.',
      'TIKTOK_SHORT_UNRESOLVED': 'Chưa xác định được bài đăng từ liên kết rút gọn. Hãy mở trong ứng dụng gốc rồi sao chép liên kết trực tiếp để thử lại.',
      'NETWORK_ERROR': 'Không kết nối được. Kiểm tra mạng rồi thử lại.',
    },
    'en': {
      'UNSAFE_SOURCE_REDIRECT': 'The link redirects to an unsupported address. Open it in the original app and copy the direct post link again.',
      'INVALID_SOURCE_URL': 'The link is invalid or unsupported. Use an HTTPS link from YouTube, TikTok, or SoundCloud.',
      'SOURCE_UNAVAILABLE': 'This content is unavailable. Check the link in the original app or choose different content.',
      'TIKTOK_IMPORT_DISABLED': 'TikTok link recognition is currently unavailable. Please try again later.',
      'SOUNDCLOUD_IMPORT_DISABLED': 'SoundCloud link recognition is currently unavailable. Please try again later.',
      'SOURCE_ACCESS_CHECK_REQUIRED': 'The source requires an access check. Open the content in the original app; TMusic cannot download it right now.',
      'SOURCE_AUTHENTICATION_REQUIRED': 'The source requires sign-in or authorization. TMusic cannot use your source account session to download it.',
      'SOURCE_ACCESS_FORBIDDEN': 'The source denied this download request (403). Open the link in the original app or choose a source that permits downloading.',
      'SOURCE_NETWORK_ERROR': 'The connection to the source was interrupted. Check your network and try again later.',
      'SOURCE_RESTRICTED_OR_UNSUPPORTED': 'The source marks this content as private, restricted, or unsupported for download. TMusic cannot download it.',
      'SOURCE_RESPONSE_UNSUPPORTED': 'Could not read the source response. Try again later or open the content in the original app.',
      'SOURCE_AUDIO_UNAVAILABLE': 'This post does not provide publicly downloadable audio. You can still open the content in the original app.',
      'RATE_LIMITED': 'Too many requests. Please wait a moment and try again.',
      'PROVIDER_TEMPORARILY_UNAVAILABLE':
          'The source is temporarily unavailable. Please try again later.',
      'TIKTOK_SHORT_UNRESOLVED': 'Could not find the post from this short link. Open it in the original app and copy the direct post link to retry.',
      'NETWORK_ERROR': 'Unable to connect. Check your connection and retry.',
    },
  };

  for (final locale in messages.entries) {
    testWidgets('provider failures have actionable ${locale.key} copy', (
      tester,
    ) async {
      late BuildContext messageContext;
      await tester.pumpWidget(
        MaterialApp(
          locale: Locale(locale.key),
          supportedLocales: const [Locale('vi'), Locale('en')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          home: Builder(
            builder: (context) {
              messageContext = context;
              return const SizedBox.shrink();
            },
          ),
        ),
      );
      await tester.pumpAndSettle();

      for (final failure in locale.value.entries) {
        final actual = errorText(
          messageContext,
          ApiFailure(
            failure.key,
            status: failure.key == 'RATE_LIMITED' ? 429 : 422,
            detail: 'Diagnostic detail must not reach the UI: fixture-only',
          ),
        );
        expect(actual, failure.value, reason: failure.key);
        expect(actual, isNot(contains('Diagnostic detail')));
        expect(
          errorText(
            messageContext,
            ApiFailure(failure.key.toLowerCase(), status: 503),
          ),
          failure.value,
          reason: '${failure.key} should be case insensitive',
        );
      }
      for (final alias in [
        'INVALID_TIKTOK_URL',
        'INVALID_SOUNDCLOUD_URL',
        'UNSUPPORTED_SOURCE_URL',
      ]) {
        expect(
          errorText(messageContext, ApiFailure(alias, status: 422)),
          locale.value['INVALID_SOURCE_URL'],
        );
      }
      for (final alias in [
        'SOUNDCLOUD_SHORT_UNRESOLVED',
        'TOO_MANY_SOURCE_REDIRECTS',
      ]) {
        expect(
          errorText(messageContext, ApiFailure(alias, status: 422)),
          locale.value['TIKTOK_SHORT_UNRESOLVED'],
        );
      }
      for (final code in ['NETWORK_ERROR', 'UNKNOWN_ERROR']) {
        expect(
          errorText(
            messageContext,
            ApiFailure(code, status: 429, detail: 'Untrusted diagnostic'),
          ),
          locale.value['RATE_LIMITED'],
          reason: 'HTTP 429 must remain readable without a JSON error body',
        );
      }

      expect(
        downloadFailureText(messageContext, 'SOURCE_ACCESS_CHECK_REQUIRED'),
        locale.value['SOURCE_ACCESS_CHECK_REQUIRED'],
      );
      for (final code in [
        'SOURCE_AUTHENTICATION_REQUIRED',
        'SOURCE_ACCESS_FORBIDDEN',
        'SOURCE_NETWORK_ERROR',
      ]) {
        expect(downloadFailureText(messageContext, code), locale.value[code]);
      }
      expect(
        downloadFailureText(messageContext, 'source_audio_unavailable'),
        locale.value['SOURCE_AUDIO_UNAVAILABLE'],
      );
      expect(
        downloadFailureText(messageContext, 'SOURCE_RESPONSE_UNSUPPORTED'),
        locale.value['SOURCE_RESPONSE_UNSUPPORTED'],
      );
      expect(
        downloadFailureText(messageContext, null),
        locale.key == 'vi'
            ? 'Chưa trích xuất được MP3 (nguồn bị giới hạn hoặc mạng gián đoạn). Bạn vẫn có thể mở trên ứng dụng gốc hoặc thử lại.'
            : 'Could not extract MP3 (source restricted or network interrupted). You can still open in the source app or retry.',
      );
      expect(
        downloadFailureText(messageContext, 'AUDIO_CONVERSION_FAILED'),
        locale.key == 'vi'
            ? 'Không chuyển đổi được âm thanh sang MP3. Bạn có thể mở trên ứng dụng gốc hoặc chọn nguồn khác.'
            : 'Could not convert the audio stream to MP3. You can open it in the original app or try another source.',
      );
      expect(isRetryableDownloadError('SOURCE_ACCESS_CHECK_REQUIRED'), isFalse);
      expect(isRetryableDownloadError('SOURCE_AUTHENTICATION_REQUIRED'), isFalse);
      expect(isRetryableDownloadError('SOURCE_ACCESS_FORBIDDEN'), isFalse);
      expect(isRetryableDownloadError('SOURCE_NETWORK_ERROR'), isTrue);
      expect(isRetryableDownloadError('AUDIO_STORAGE_REJECTED'), isFalse);
      expect(isRetryableDownloadError('UNRECOGNIZED_PERMANENT_ERROR'), isFalse);
      expect(isRetryableDownloadError('SOURCE_AUDIO_UNAVAILABLE'), isFalse);
      expect(isRetryableDownloadError('AUDIO_CONVERSION_FAILED'), isFalse);
      expect(isRetryableDownloadError('RATE_LIMITED'), isTrue);
      expect(isRetryableDownloadError('AUDIO_EXTRACTION_FAILED'), isTrue);
      expect(isRetryableDownloadError('network_error'), isTrue);
    });
  }
}
