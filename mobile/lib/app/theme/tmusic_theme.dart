import 'package:flutter/cupertino.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

@immutable
class TMusicTokens extends ThemeExtension<TMusicTokens> {
  const TMusicTokens({
    required this.success,
    required this.successContainer,
    required this.warning,
    required this.warningContainer,
    required this.heroShadow,
    required this.floatingShadow,
  });

  final Color success;
  final Color successContainer;
  final Color warning;
  final Color warningContainer;
  final List<BoxShadow> heroShadow;
  final List<BoxShadow> floatingShadow;

  @override
  TMusicTokens copyWith({
    Color? success,
    Color? successContainer,
    Color? warning,
    Color? warningContainer,
    List<BoxShadow>? heroShadow,
    List<BoxShadow>? floatingShadow,
  }) => TMusicTokens(
    success: success ?? this.success,
    successContainer: successContainer ?? this.successContainer,
    warning: warning ?? this.warning,
    warningContainer: warningContainer ?? this.warningContainer,
    heroShadow: heroShadow ?? this.heroShadow,
    floatingShadow: floatingShadow ?? this.floatingShadow,
  );

  @override
  TMusicTokens lerp(ThemeExtension<TMusicTokens>? other, double t) {
    if (other is! TMusicTokens) return this;
    return TMusicTokens(
      success: Color.lerp(success, other.success, t)!,
      successContainer: Color.lerp(successContainer, other.successContainer, t)!,
      warning: Color.lerp(warning, other.warning, t)!,
      warningContainer: Color.lerp(warningContainer, other.warningContainer, t)!,
      heroShadow: t < 0.5 ? heroShadow : other.heroShadow,
      floatingShadow: t < 0.5 ? floatingShadow : other.floatingShadow,
    );
  }
}

/// Premium Spotify-Inspired Flutter Music App UI/UX Design System.
/// Dark Mode: #121212 charcoal canvas, #181818 / #282828 card surfaces,
/// #FFFFFF crisp typography, #B3B3B3 secondary metadata, #1DB954 Spotify Green.
class TMusicTheme {
  static const Color spotifyGreen = Color(0xFF1DB954);
  static const Color spotifyGreenBright = Color(0xFF1ED760);
  static const Color primaryNavy = Color(0xFF1E3264);
  static const Color softBlue = Color(0xFF2D46B9);

  // Dark Charcoal Tokens
  static const Color darkCanvas = Color(0xFF121212);
  static const Color darkSurface = Color(0xFF181818);
  static const Color darkCard = Color(0xFF282828);
  static const Color darkElevated = Color(0xFF333333);
  static const Color textMainDark = Color(0xFFFFFFFF);
  static const Color textSecondaryDark = Color(0xFFB3B3B3);

  // Clean Light Tokens
  static const Color lightCanvas = Color(0xFFF6F6F6);
  static const Color lightSurface = Color(0xFFFFFFFF);
  static const Color lightCard = Color(0xFFEBEBEB);
  static const Color lightElevated = Color(0xFFE0E0E0);
  static const Color textMainLight = Color(0xFF121212);
  static const Color textSecondaryLight = Color(0xFF5E5E5E);

  static const Color success = spotifyGreen;
  static const Color successDark = spotifyGreen;
  static const Color errorLight = Color(0xFFD32F2F);
  static const Color errorDark = Color(0xFFFF6B6B);

  static ThemeData get light => _build(Brightness.light);
  static ThemeData get dark => _build(Brightness.dark);

  static ThemeData _build(Brightness brightness) {
    final isDark = brightness == Brightness.dark;
    final scaffoldBg = isDark ? darkCanvas : lightCanvas;
    final surfaceColor = isDark ? darkSurface : lightSurface;
    final surfaceLow = isDark ? darkCard : lightCard;
    final surfaceCont = isDark ? darkElevated : lightElevated;
    final onSurfaceColor = isDark ? textMainDark : textMainLight;
    final secondaryText = isDark ? textSecondaryDark : textSecondaryLight;
    final outlineColor = isDark
        ? const Color(0xFF2E2E2E)
        : const Color(0xFFDDDDDD);

    final scheme = ColorScheme.fromSeed(
      seedColor: spotifyGreen,
      brightness: brightness,
    ).copyWith(
      primary: spotifyGreen,
      onPrimary: Colors.black,
      secondary: spotifyGreenBright,
      onSecondary: Colors.black,
      primaryContainer: isDark
          ? const Color(0xFF163D26)
          : const Color(0xFFD7F5E1),
      onPrimaryContainer: isDark ? spotifyGreenBright : const Color(0xFF0B4F25),
      secondaryContainer: isDark ? darkCard : lightCard,
      onSecondaryContainer: onSurfaceColor,
      surface: surfaceColor,
      onSurface: onSurfaceColor,
      onSurfaceVariant: secondaryText,
      surfaceContainerLowest: scaffoldBg,
      surfaceContainerLow: surfaceLow,
      surfaceContainer: surfaceCont,
      surfaceContainerHigh: isDark
          ? const Color(0xFF3E3E3E)
          : const Color(0xFFD4D4D4),
      outline: isDark ? const Color(0xFF727272) : const Color(0xFF8C8C8C),
      outlineVariant: outlineColor,
      error: isDark ? errorDark : errorLight,
    );

    final tokens = TMusicTokens(
      success: spotifyGreen,
      successContainer: isDark
          ? const Color(0xFF143823)
          : const Color(0xFFDDF6E6),
      warning: isDark ? const Color(0xFFFFB74D) : const Color(0xFF9A5B00),
      warningContainer: isDark
          ? const Color(0xFF332614)
          : const Color(0xFFFFF1DB),
      heroShadow: isDark
          ? const [
              BoxShadow(
                color: Color(0x88000000),
                blurRadius: 30,
                offset: Offset(0, 14),
              ),
            ]
          : const [
              BoxShadow(
                color: Color(0x22000000),
                blurRadius: 22,
                offset: Offset(0, 10),
              ),
            ],
      floatingShadow: isDark
          ? const [
              BoxShadow(
                color: Color(0x99000000),
                blurRadius: 18,
                offset: Offset(0, 6),
              ),
            ]
          : const [
              BoxShadow(
                color: Color(0x24000000),
                blurRadius: 14,
                offset: Offset(0, 5),
              ),
            ],
    );

    final overlay = SystemUiOverlayStyle(
      statusBarColor: Colors.transparent,
      statusBarIconBrightness: isDark ? Brightness.light : Brightness.dark,
      statusBarBrightness: isDark ? Brightness.dark : Brightness.light,
      systemNavigationBarColor: isDark ? const Color(0xFF0A0A0A) : surfaceColor,
      systemNavigationBarIconBrightness:
          isDark ? Brightness.light : Brightness.dark,
    );

    return ThemeData(
      useMaterial3: true,
      colorScheme: scheme,
      scaffoldBackgroundColor: scaffoldBg,
      visualDensity: VisualDensity.standard,
      extensions: [tokens],
      pageTransitionsTheme: const PageTransitionsTheme(
        builders: {
          TargetPlatform.android: FadeUpwardsPageTransitionsBuilder(),
          TargetPlatform.iOS: CupertinoPageTransitionsBuilder(),
        },
      ),
      appBarTheme: AppBarTheme(
        backgroundColor: Colors.transparent,
        foregroundColor: onSurfaceColor,
        surfaceTintColor: Colors.transparent,
        systemOverlayStyle: overlay,
        elevation: 0,
        scrolledUnderElevation: 0,
        centerTitle: false,
        titleTextStyle: TextStyle(
          fontSize: 23,
          fontWeight: FontWeight.w800,
          letterSpacing: -0.5,
          color: onSurfaceColor,
          height: 1.2,
        ),
      ),
      cardTheme: CardThemeData(
        color: surfaceColor,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        margin: EdgeInsets.zero,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(12),
        ),
      ),
      bottomSheetTheme: BottomSheetThemeData(
        backgroundColor: isDark ? const Color(0xFF1E1E1E) : surfaceColor,
        surfaceTintColor: Colors.transparent,
        modalBackgroundColor: isDark ? const Color(0xFF1E1E1E) : surfaceColor,
        dragHandleColor: secondaryText.withValues(alpha: 0.45),
        shape: const RoundedRectangleBorder(
          borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
        ),
      ),
      dialogTheme: DialogThemeData(
        backgroundColor: isDark ? const Color(0xFF242424) : surfaceColor,
        surfaceTintColor: Colors.transparent,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(18)),
      ),
      textTheme: TextTheme(
        headlineLarge: TextStyle(
          fontSize: 25,
          fontWeight: FontWeight.w800,
          height: 1.2,
          letterSpacing: -0.5,
          color: onSurfaceColor,
        ),
        headlineSmall: TextStyle(
          fontSize: 20,
          fontWeight: FontWeight.w800,
          height: 1.24,
          letterSpacing: -0.3,
          color: onSurfaceColor,
        ),
        titleLarge: TextStyle(
          fontSize: 18,
          fontWeight: FontWeight.w700,
          height: 1.26,
          letterSpacing: -0.2,
          color: onSurfaceColor,
        ),
        titleMedium: TextStyle(
          fontSize: 15,
          fontWeight: FontWeight.w600,
          height: 1.3,
          color: onSurfaceColor,
        ),
        bodyLarge: TextStyle(
          fontSize: 15,
          height: 1.46,
          color: onSurfaceColor,
        ),
        bodyMedium: TextStyle(
          fontSize: 14,
          height: 1.42,
          color: onSurfaceColor,
        ),
        bodySmall: TextStyle(
          fontSize: 12.5,
          height: 1.35,
          fontFeatures: const [FontFeature.tabularFigures()],
          color: secondaryText,
        ),
        labelLarge: TextStyle(
          fontSize: 13.5,
          fontWeight: FontWeight.w700,
          height: 1.25,
          color: onSurfaceColor,
        ),
        labelSmall: TextStyle(
          fontSize: 11,
          fontWeight: FontWeight.w700,
          letterSpacing: 0.8,
          color: secondaryText,
        ),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: surfaceLow,
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(10),
          borderSide: BorderSide.none,
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(10),
          borderSide: BorderSide.none,
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(10),
          borderSide: const BorderSide(color: spotifyGreen, width: 1.6),
        ),
        errorBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(10),
          borderSide: BorderSide(color: scheme.error, width: 1.3),
        ),
        focusedErrorBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(10),
          borderSide: BorderSide(color: scheme.error, width: 1.6),
        ),
        contentPadding: const EdgeInsets.symmetric(
          horizontal: 16,
          vertical: 14,
        ),
        alignLabelWithHint: true,
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: spotifyGreen,
          foregroundColor: Colors.black,
          minimumSize: const Size(48, 50),
          padding: const EdgeInsets.symmetric(horizontal: 22, vertical: 13),
          textStyle: const TextStyle(fontSize: 15, fontWeight: FontWeight.w800),
          shape: const StadiumBorder(),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          minimumSize: const Size(48, 48),
          padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 12),
          side: BorderSide(color: scheme.outline),
          foregroundColor: onSurfaceColor,
          textStyle: const TextStyle(fontSize: 14, fontWeight: FontWeight.w700),
          shape: const StadiumBorder(),
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          minimumSize: const Size(48, 48),
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
          shape: const StadiumBorder(),
        ),
      ),
      iconButtonTheme: IconButtonThemeData(
        style: IconButton.styleFrom(minimumSize: const Size(48, 48)),
      ),
      // Spotify Library Pill Chips: solid green when selected, dark outlined pill when unselected!
      chipTheme: ChipThemeData(
        backgroundColor: Colors.transparent,
        selectedColor: spotifyGreen,
        secondarySelectedColor: spotifyGreen,
        side: WidgetStateBorderSide.resolveWith((states) {
          if (states.contains(WidgetState.selected)) return BorderSide.none;
          return BorderSide(
            color: scheme.outline.withValues(alpha: 0.65),
          );
        }),
        shape: const StadiumBorder(),
        labelStyle: TextStyle(
          fontSize: 13,
          fontWeight: FontWeight.w600,
          color: onSurfaceColor,
        ),
        secondaryLabelStyle: const TextStyle(
          fontSize: 13,
          fontWeight: FontWeight.w700,
          color: Colors.black,
        ),
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 7),
      ),
      navigationBarTheme: NavigationBarThemeData(
        height: 60,
        backgroundColor: Colors.transparent,
        surfaceTintColor: Colors.transparent,
        indicatorColor: Colors.transparent,
        labelBehavior: NavigationDestinationLabelBehavior.alwaysShow,
        labelTextStyle: WidgetStateProperty.resolveWith((states) {
          final selected = states.contains(WidgetState.selected);
          return TextStyle(
            fontSize: 11,
            fontWeight: selected ? FontWeight.w700 : FontWeight.w500,
            color: selected ? onSurfaceColor : secondaryText,
          );
        }),
        iconTheme: WidgetStateProperty.resolveWith((states) {
          final selected = states.contains(WidgetState.selected);
          return IconThemeData(
            size: 24,
            color: selected ? onSurfaceColor : secondaryText,
          );
        }),
      ),
      dividerTheme: DividerThemeData(
        color: outlineColor,
        thickness: 1,
        space: 22,
      ),
      snackBarTheme: SnackBarThemeData(
        backgroundColor: isDark ? darkCard : textMainLight,
        contentTextStyle: const TextStyle(
          color: Colors.white,
          fontWeight: FontWeight.w600,
        ),
        behavior: SnackBarBehavior.floating,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
      ),
    );
  }
}
