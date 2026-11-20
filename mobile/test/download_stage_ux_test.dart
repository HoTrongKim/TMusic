import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:tmusic/core/widgets/common.dart';
import 'package:tmusic/shared/models/library_models.dart';

void main() {
  testWidgets('download stage labels render accurate Vietnamese progress and failure states', (
    tester,
  ) async {
    late BuildContext ctx;
    await tester.pumpWidget(
      MaterialApp(
        locale: const Locale('vi'),
        supportedLocales: const [Locale('vi'), Locale('en')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        home: Builder(
          builder: (context) {
            ctx = context;
            return const SizedBox.shrink();
          },
        ),
      ),
    );
    await tester.pumpAndSettle();

    AudioDownloadJob makeJob(
      String status, {
      int progress = 0,
      String? errorCode,
      bool? accessRestricted,
    }) => AudioDownloadJob(
      id: 'job-1',
      songId: 'song-1',
      provider: 'youtube',
      canonicalUrl: 'https://www.youtube.com/watch?v=aqz-KE-bpKQ',
      status: status,
      progressPercent: progress,
      audioReady: status == 'COMPLETE',
      errorCode: errorCode,
      accessRestricted: accessRestricted,
    );

    expect(
      downloadStageLabel(ctx, makeJob('QUEUED', progress: 0)),
      'Đang chuẩn bị tải xuống…',
    );
    expect(
      downloadStageLabel(ctx, makeJob('RUNNING', progress: 42)),
      'Đang tải âm thanh… (42%)',
    );
    expect(
      downloadStageLabel(ctx, makeJob('RUNNING', progress: 95)),
      'Đang chuyển đổi sang MP3… (95%)',
    );
    expect(
      downloadStageLabel(ctx, makeJob('COMPLETE', progress: 100)),
      'Tải xuống thành công!',
    );
    expect(
      downloadStageLabel(ctx, makeJob('CANCELED')),
      'Đã hủy tải xuống',
    );
    expect(
      downloadStageLabel(
        ctx,
        makeJob('FAILED', errorCode: 'SOURCE_ACCESS_CHECK_REQUIRED'),
      ),
      'Không thể truy cập nguồn',
    );
    expect(
      downloadStageLabel(
        ctx,
        makeJob('FAILED', errorCode: 'SOURCE_NETWORK_ERROR'),
      ),
      'Tải xuống thất bại',
    );
  });

  test('AudioDownloadJob parses errorCategory and accessRestricted from API JSON', () {
    final job = AudioDownloadJob.fromJson({
      'id': 'job-2',
      'songId': 'song-2',
      'provider': 'youtube',
      'canonicalUrl': 'https://www.youtube.com/watch?v=aqz-KE-bpKQ',
      'status': 'FAILED',
      'progressPercent': 5,
      'errorCode': 'SOURCE_ACCESS_CHECK_REQUIRED',
      'errorCategory': 'SourceAccessCheckRequired',
      'accessRestricted': true,
      'retryable': false,
      'audioReady': false,
    });

    expect(job.isAccessRestricted, isTrue);
    expect(job.isRetryable, isFalse);
    expect(job.errorCategory, 'SourceAccessCheckRequired');
  });
}
