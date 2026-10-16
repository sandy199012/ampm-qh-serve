import 'dart:convert';
import 'dart:io';
import 'dart:math';
import 'package:battery_plus/battery_plus.dart';
import 'package:device_info_plus/device_info_plus.dart';
import 'package:http/http.dart' as http;
import 'package:shared_preferences/shared_preferences.dart';
import 'update_service.dart';

// Mobile equivalent of AMPM_PC_Agent.ps1 (the Windows/Mac PC Inventory agent)
// — instead of a script run on a PC, this runs inside the Helpdesk app itself
// and quietly reports this phone/tablet's basic details to the website's
// Endpoint Monitor -> "Mobile Devices" tab every time the app is opened and
// someone is logged in. It never shows anything to the user and never throws
// — any failure here must not affect login/tickets, which is the app's real job.
//
// Important limitation (by platform design, not something this app can work
// around): neither Android nor iOS lets a normal app read the phone's real
// Serial Number or IMEI without the device being enrolled in an MDM (Mobile
// Device Management) solution — that's a separate, bigger setup (Apple
// Business Manager / Android Enterprise + a product like Intune/Jamf/Hexnode/
// Headwind MDM), not something this app can add on its own. What IS available
// to a normal app, and what this sends: model, manufacturer, OS version,
// battery %, and a random ID this app generates once and keeps locally so the
// server can recognize "the same phone reporting again" across app opens.
class DeviceReportService {
  static const String _serverUrl =
      'https://ampm-qh-serve-1.onrender.com/api/endpoints/report-mobile';
  // The shared key is supplied at build time (GitHub secret MOBILE_KEY ->
  // --dart-define); the old default is kept so a plain local build still works.
  static const String _keyFromBuild = String.fromEnvironment('MOBILE_KEY');
  static String get _agentKey => _keyFromBuild.isEmpty ? 'AMPM-AGENT-2026' : _keyFromBuild;
  static const String _deviceIdPrefKey = 'ampm_device_id';

  /// Fire-and-forget — call this after a successful login and just don't
  /// await it (or await it without caring about the result). Every possible
  /// failure is swallowed here on purpose.
  static Future<void> reportDeviceInfo() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      final deviceId = await _getOrCreateDeviceId(prefs);

      String model = '', manufacturer = '', os = '', osBuild = '';
      try {
        final deviceInfo = DeviceInfoPlugin();
        if (Platform.isAndroid) {
          final a = await deviceInfo.androidInfo;
          model = a.model;
          manufacturer = a.manufacturer;
          os = 'Android ${a.version.release}';
          osBuild = 'SDK ${a.version.sdkInt}';
        } else if (Platform.isIOS) {
          final i = await deviceInfo.iosInfo;
          model = i.utsname.machine;
          manufacturer = 'Apple';
          os = '${i.systemName} ${i.systemVersion}';
          osBuild = i.utsname.release;
        } else {
          return; // desktop/web builds - nothing useful to report
        }
      } catch (_) {
        return; // couldn't read device info at all - skip this report silently
      }

      String battery = '';
      try {
        final level = await Battery().batteryLevel;
        if (level >= 0) battery = '$level';
      } catch (_) {}

      final username = prefs.getString('username') ?? '';
      final empId = prefs.getString('emp_id') ?? '';
      final empName = prefs.getString('emp_name') ?? '';

      final payload = jsonEncode({
        'key': _agentKey,
        'deviceId': deviceId,
        'model': model,
        'manufacturer': manufacturer,
        'os': os,
        'osBuild': osBuild,
        'battery': battery,
        'user': username,
        'empId': empId,
        'empName': empName,
        'appVersion': UpdateService.currentVersion,
      });

      await http
          .post(Uri.parse(_serverUrl),
              headers: {'Content-Type': 'application/json'}, body: payload)
          .timeout(const Duration(seconds: 15));
    } catch (_) {
      // Silent by design - device reporting must never disrupt the app.
    }
  }

  /// A random ID generated once on first run and kept in local storage. Not
  /// the phone's real serial number (no normal app can read that - see the
  /// class comment above) - just a stable way for the server to tell "this
  /// same install reporting again" from "a different phone".
  static Future<String> _getOrCreateDeviceId(SharedPreferences prefs) async {
    var id = prefs.getString(_deviceIdPrefKey);
    if (id != null && id.isNotEmpty) return id;
    final rnd = Random.secure();
    final bytes = List<int>.generate(16, (_) => rnd.nextInt(256));
    id = bytes.map((b) => b.toRadixString(16).padLeft(2, '0')).join();
    await prefs.setString(_deviceIdPrefKey, id);
    return id;
  }
}
