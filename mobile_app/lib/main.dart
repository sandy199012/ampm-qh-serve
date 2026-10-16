import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'screens/home_screen.dart';
import 'screens/setup_screen.dart';
import 'screens/admin_home_screen.dart';
import 'screens/change_password_screen.dart';
import 'app_theme.dart';
import 'services/api_service.dart';
import 'services/device_report_service.dart';
import 'services/update_service.dart';
import 'widgets/update_ui.dart';

// Lets the update dialog open from anywhere (app start / coming back to front).
final GlobalKey<NavigatorState> appNavigatorKey = GlobalKey<NavigatorState>();

void main() {
  runApp(const AMPMHelpdeskApp());
}

class AMPMHelpdeskApp extends StatelessWidget {
  const AMPMHelpdeskApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'AMPM IT Helpdesk',
      navigatorKey: appNavigatorKey,
      builder: (context, child) => UpdateLifecycleWatcher(
          navKey: appNavigatorKey, child: child ?? const SizedBox.shrink()),
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        useMaterial3: true,
        fontFamily: 'Roboto',
        scaffoldBackgroundColor: AppColors.bg,
        colorScheme: ColorScheme.fromSeed(
          seedColor: AppColors.blue,
          primary: AppColors.blue,
          secondary: AppColors.cyan,
          tertiary: AppColors.purple,
          surface: Colors.white,
        ),
        appBarTheme: const AppBarTheme(
          backgroundColor: AppColors.navy,
          foregroundColor: Colors.white,
          elevation: 0,
          centerTitle: false,
          titleTextStyle: TextStyle(color: Colors.white, fontSize: 16, fontWeight: FontWeight.bold),
        ),
        elevatedButtonTheme: ElevatedButtonThemeData(
          style: ElevatedButton.styleFrom(
            elevation: 0,
            padding: const EdgeInsets.symmetric(vertical: 14),
            shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
          ),
        ),
        textButtonTheme: TextButtonThemeData(
          style: TextButton.styleFrom(foregroundColor: AppColors.blue),
        ),
        floatingActionButtonTheme: FloatingActionButtonThemeData(
          backgroundColor: AppColors.cyan,
          foregroundColor: Colors.white,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(18)),
        ),
        inputDecorationTheme: InputDecorationTheme(
          filled: true,
          fillColor: Colors.white,
          border: OutlineInputBorder(
              borderRadius: BorderRadius.circular(14), borderSide: const BorderSide(color: AppColors.cardBorder)),
          enabledBorder: OutlineInputBorder(
              borderRadius: BorderRadius.circular(14), borderSide: const BorderSide(color: AppColors.cardBorder)),
          focusedBorder: OutlineInputBorder(
              borderRadius: BorderRadius.circular(14), borderSide: const BorderSide(color: AppColors.blue, width: 1.8)),
        ),
        chipTheme: ChipThemeData(
          backgroundColor: const Color(0xFFEFEAFB),
          selectedColor: AppColors.blue.withOpacity(.16),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
          side: BorderSide.none,
        ),
        snackBarTheme: SnackBarThemeData(
          behavior: SnackBarBehavior.floating,
          backgroundColor: AppColors.navy,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
        ),
        progressIndicatorTheme: const ProgressIndicatorThemeData(color: AppColors.blue),
        dividerTheme: const DividerThemeData(color: AppColors.cardBorder),
        pageTransitionsTheme: const PageTransitionsTheme(builders: {
          TargetPlatform.android: FadeUpwardsPageTransitionsBuilder(),
        }),
      ),
      home: const SplashScreen(),
    );
  }
}

class SplashScreen extends StatefulWidget {
  const SplashScreen({super.key});
  @override
  State<SplashScreen> createState() => _SplashScreenState();
}

class _SplashScreenState extends State<SplashScreen> {
  bool _visible = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) setState(() => _visible = true);
    });
    _checkSetup();
  }

  Future<void> _checkSetup() async {
    await UpdateService.loadCurrent();
    await Future.delayed(const Duration(seconds: 2));
    final prefs = await SharedPreferences.getInstance();
    final username = prefs.getString('username') ?? '';
    final password = prefs.getString('password') ?? '';
    bool isAdmin = prefs.getBool('is_admin') ?? false;
    bool mustChangePassword = false;

    // Refresh role/permission from the server on every app open (admin/
    // employee status, Employee Code, name/dept as assigned on the Users
    // page) — so that when Sandy makes someone admin or links an Employee
    // Code, that employee gets it right away next time they open the app,
    // without needing to log out and back in. If the server is unreachable,
    // the previously cached status is used instead (so the app still works
    // offline).
    if (username.isNotEmpty && password.isNotEmpty) {
      final fresh = await ApiService.login(username, password);
      if (fresh != null) {
        isAdmin = fresh['isAdmin'] == 'true';
        mustChangePassword = fresh['mustChangePassword'] == 'true';
        await prefs.setString('emp_id', fresh['empId'] ?? '');
        await prefs.setString('emp_name', fresh['name'] ?? '');
        await prefs.setString('emp_dept', fresh['dept'] ?? '');
        await prefs.setString('emp_desig', fresh['desig'] ?? '');
        await prefs.setBool('is_admin', isAdmin);
      }
      // Fire-and-forget: reports this phone/tablet's basic details (model,
      // OS version, battery) into the website's Endpoint Monitor -> Mobile
      // Devices tab, tagged with whoever's logged in. Deliberately not
      // awaited - this must never delay opening the app, and any failure
      // here is swallowed inside the service itself.
      DeviceReportService.reportDeviceInfo();
    }

    if (mounted) {
      if (username.isEmpty) {
        Navigator.pushReplacement(context,
            MaterialPageRoute(builder: (_) => const SetupScreen()));
      } else if (mustChangePassword) {
        Navigator.pushReplacement(context,
            MaterialPageRoute(builder: (_) => const ChangePasswordScreen(forced: true)));
      } else if (isAdmin) {
        Navigator.pushReplacement(context,
            MaterialPageRoute(builder: (_) => const AdminHomeScreen()));
      } else {
        Navigator.pushReplacement(context,
            MaterialPageRoute(builder: (_) => const HomeScreen()));
      }
      // Look for a newer app version (quiet; shows a dialog only if one exists).
      UpdateUi.checkAndPrompt(appNavigatorKey);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Container(
        decoration: const BoxDecoration(gradient: AppColors.navyGradient),
        child: Center(
          child: AnimatedOpacity(
            opacity: _visible ? 1 : 0,
            duration: const Duration(milliseconds: 700),
            child: AnimatedSlide(
              offset: _visible ? Offset.zero : const Offset(0, 0.05),
              duration: const Duration(milliseconds: 700),
              curve: Curves.easeOutCubic,
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  const AppLogoBadge(size: 96),
                  const SizedBox(height: 28),
                  const Text('AMPM IT Helpdesk',
                      style: TextStyle(color: Colors.white, fontSize: 24, fontWeight: FontWeight.bold, letterSpacing: .2)),
                  const SizedBox(height: 8),
                  Text('AMPM Fashions Pvt. Ltd.',
                      style: TextStyle(color: Colors.white.withOpacity(.65), fontSize: 13, letterSpacing: .5)),
                  const SizedBox(height: 48),
                  const SizedBox(
                    width: 26, height: 26,
                    child: CircularProgressIndicator(strokeWidth: 2.4, color: AppColors.cyan),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
