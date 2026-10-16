import 'dart:convert';
import 'dart:io';
import 'package:crypto/crypto.dart';
import 'package:flutter/foundation.dart';
import 'package:http/http.dart' as http;
import 'package:open_filex/open_filex.dart';
import 'package:package_info_plus/package_info_plus.dart';
import 'package:path_provider/path_provider.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// One published release of the app (built by GitHub Actions, announced by
/// the company server).
class UpdateInfo {
  final String version;      // e.g. "1.1.7"
  final String notes;        // what's new (the commit message of the release)
  final String apkUrl;       // direct download link of the APK
  final int sizeBytes;       // 0 when unknown
  final String sha256;       // '' when the release did not publish one
  final bool mandatory;      // release notes contain [mandatory]
  const UpdateInfo({
    required this.version,
    required this.notes,
    required this.apkUrl,
    required this.sizeBytes,
    required this.sha256,
    required this.mandatory,
  });
}

/// Self-update for the Helpdesk app: asks GitHub whether a newer APK exists,
/// downloads it inside the app and hands it to Android's installer. The user
/// taps "Install" once — no uninstalling, no re-sharing the APK by WhatsApp.
/// (Android never allows an app to install itself silently; that needs MDM.)
class UpdateService {
  /// The company server: it knows the newest published app version and
  /// forwards the APK download (works whether the GitHub repository is public or private).
  static const String serverBase = 'https://ampm-qh-serve-1.onrender.com';
  static const String _serverLatest = '$serverBase/api/app/latest';

  /// Fallback when the server is asleep/unreachable: GitHub releases of the
  /// repository (works only while that repository is public).
  static const String githubRepo = 'sandy199012/ampm-qh-serve';
  static const String _githubReleases =
      'https://api.github.com/repos/$githubRepo/releases?per_page=20';

  /// Page that always serves the newest APK (also the link to share for a first install).
  static const String downloadPage = '$serverBase/app';

  static const Duration _minGap = Duration(hours: 4);

  /// Set when a newer version is known; the home screens show a banner for it.
  static final ValueNotifier<UpdateInfo?> available = ValueNotifier<UpdateInfo?>(null);

  static String currentVersion = '';
  static String currentBuild = '';
  static bool _checking = false;

  static bool get supported => !kIsWeb && Platform.isAndroid;

  /// Reads the version of the installed app (from the APK itself).
  static Future<void> loadCurrent() async {
    try {
      final p = await PackageInfo.fromPlatform();
      currentVersion = p.version;
      currentBuild = p.buildNumber;
    } catch (_) {}
  }

  /// Dotted-number comparison: >0 when [a] is newer than [b].
  static int compareVersions(String a, String b) {
    List<int> parts(String v) => v
        .replaceFirst(RegExp(r'^[vV]'), '')
        .split(RegExp(r'[.+\-]'))
        .map((e) => int.tryParse(e) ?? 0)
        .toList();
    final x = parts(a), y = parts(b);
    final n = x.length > y.length ? x.length : y.length;
    for (var i = 0; i < n; i++) {
      final p = i < x.length ? x[i] : 0;
      final q = i < y.length ? y[i] : 0;
      if (p != q) return p > q ? 1 : -1;
    }
    return 0;
  }

  /// Asks the company server (or GitHub as a fallback) for the latest release. Returns the release when it is newer
  /// than the installed app, otherwise null.
  /// [force] skips the "checked recently" limit and lets errors through (for
  /// the manual "Check for updates" button). When not forced, every problem is
  /// swallowed — a failed update check must never disturb the app.
  static Future<UpdateInfo?> check({bool force = false}) async {
    if (!supported) return null;
    if (_checking) return available.value;
    _checking = true;
    try {
      final prefs = await SharedPreferences.getInstance();
      if (!force) {
        final last = prefs.getInt('upd_last_check') ?? 0;
        if (DateTime.now().millisecondsSinceEpoch - last < _minGap.inMilliseconds) {
          return available.value;
        }
      }
      if (currentVersion.isEmpty) await loadCurrent();

      UpdateInfo? latest = await _fromServer();
      latest ??= await _fromGithub();
      if (latest == null) {
        throw Exception('Update server se jawab nahi mila');
      }
      await prefs.setInt('upd_last_check', DateTime.now().millisecondsSinceEpoch);

      if (compareVersions(latest.version, currentVersion) <= 0) {
        available.value = null;
        return null;
      }
      final info = latest;
      available.value = info;
      return info;
    } catch (e) {
      if (force) rethrow;
      return available.value;
    } finally {
      _checking = false;
    }
  }

  // Company server first (may need up to ~40 s to wake up on the free plan).
  static Future<UpdateInfo?> _fromServer() async {
    try {
      final res = await http.get(Uri.parse(_serverLatest), headers: {
        'Accept': 'application/json',
        'User-Agent': 'ampm-helpdesk-app',
      }).timeout(const Duration(seconds: 25));
      if (res.statusCode != 200) return null;
      final j = jsonDecode(res.body) as Map<String, dynamic>;
      final version = (j['version'] ?? '').toString();
      if (version.isEmpty) return null;
      final rel = (j['apkUrl'] ?? '/api/app/download').toString();
      return UpdateInfo(
        version: version.replaceFirst(RegExp(r'^[vV]'), ''),
        notes: (j['notes'] ?? '').toString(),
        apkUrl: rel.startsWith('http') ? rel : '$serverBase$rel',
        sizeBytes: (j['size'] is num) ? (j['size'] as num).toInt() : 0,
        sha256: (j['sha256'] ?? '').toString(),
        mandatory: j['mandatory'] == true,
      );
    } catch (_) {
      return null;
    }
  }

  // GitHub directly: newest release tagged app-v<version> that has an .apk.
  static Future<UpdateInfo?> _fromGithub() async {
    try {
      final res = await http.get(Uri.parse(_githubReleases), headers: {
        'Accept': 'application/vnd.github+json',
        'User-Agent': 'ampm-helpdesk-app',
      }).timeout(const Duration(seconds: 12));
      if (res.statusCode != 200) return null;
      final list = jsonDecode(res.body) as List;
      for (final r in list) {
        final j = r as Map<String, dynamic>;
        final tag = (j['tag_name'] ?? '').toString();
        if (!tag.toLowerCase().startsWith('app-v')) continue;
        if (j['draft'] == true || j['prerelease'] == true) continue;
        for (final a in (j['assets'] as List? ?? const [])) {
          final m = a as Map<String, dynamic>;
          if (!(m['name'] ?? '').toString().toLowerCase().endsWith('.apk')) continue;
          var body = (j['body'] ?? '').toString().replaceAll('\r', '').trim();
          var sha = '';
          final sm = RegExp(r'sha256:\s*([0-9a-fA-F]{64})').firstMatch(body);
          if (sm != null) {
            sha = sm.group(1)!.toLowerCase();
            body = body.replaceAll(sm.group(0)!, '').trim();
          }
          final mandatory = body.toLowerCase().contains('[mandatory]');
          body = body.replaceAll(RegExp(r'\[mandatory\]', caseSensitive: false), '').trim();
          return UpdateInfo(
            version: tag.substring(5),
            notes: body,
            apkUrl: (m['browser_download_url'] ?? '').toString(),
            sizeBytes: (m['size'] is num) ? (m['size'] as num).toInt() : 0,
            sha256: sha,
            mandatory: mandatory,
          );
        }
      }
    } catch (_) {}
    return null;
  }

  // ── "Baad me" (remind later) ─────────────────────────────────────────────
  static Future<bool> shouldPrompt(UpdateInfo info) async {
    if (info.mandatory) return true;
    try {
      final prefs = await SharedPreferences.getInstance();
      final v = prefs.getString('upd_snooze_ver') ?? '';
      final until = prefs.getInt('upd_snooze_until') ?? 0;
      if (v == info.version && DateTime.now().millisecondsSinceEpoch < until) return false;
    } catch (_) {}
    return true;
  }

  static Future<void> snooze(UpdateInfo info, {Duration for_ = const Duration(hours: 24)}) async {
    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setString('upd_snooze_ver', info.version);
      await prefs.setInt('upd_snooze_until',
          DateTime.now().add(for_).millisecondsSinceEpoch);
    } catch (_) {}
  }

  // ── download + install ───────────────────────────────────────────────────

  /// Downloads the APK into the app's cache folder, reporting 0..1 progress,
  /// and checks size and SHA-256 so a half-downloaded or altered file is never
  /// handed to the installer.
  static Future<File> download(UpdateInfo info, void Function(double) onProgress) async {
    final dir = await getTemporaryDirectory();
    final file = File('${dir.path}/AMPM_Helpdesk_${info.version}.apk');
    if (await file.exists()) await file.delete();

    final client = http.Client();
    try {
      final req = http.Request('GET', Uri.parse(info.apkUrl));
      req.headers['User-Agent'] = 'ampm-helpdesk-app';
      final res = await client.send(req).timeout(const Duration(seconds: 30));
      if (res.statusCode != 200) {
        throw Exception('Download fail (HTTP ${res.statusCode})');
      }
      final total = (res.contentLength != null && res.contentLength! > 0)
          ? res.contentLength!
          : info.sizeBytes;
      final sink = file.openWrite();
      var got = 0;
      try {
        await for (final chunk in res.stream.timeout(const Duration(seconds: 30))) {
          sink.add(chunk);
          got += chunk.length;
          if (total > 0) onProgress((got / total).clamp(0.0, 1.0));
        }
      } finally {
        await sink.close();
      }
      if (info.sizeBytes > 0 && got != info.sizeBytes) {
        await file.delete();
        throw Exception('Download adhura reh gaya ($got / ${info.sizeBytes} bytes) — dobara try karo');
      }
      if (info.sha256.isNotEmpty) {
        final digest = await sha256.bind(file.openRead()).first;
        if (digest.toString() != info.sha256) {
          await file.delete();
          throw Exception('File check (SHA-256) match nahi hua — install nahi kiya');
        }
      }
      onProgress(1.0);
      return file;
    } finally {
      client.close();
    }
  }

  /// Opens Android's package installer for the downloaded APK.
  /// Returns null when the installer opened, otherwise a message to show.
  static Future<String?> install(File apk) async {
    try {
      final r = await OpenFilex.open(apk.path,
          type: 'application/vnd.android.package-archive');
      switch (r.type) {
        case ResultType.done:
          return null;
        case ResultType.permissionDenied:
          return 'Install ki permission nahi mili. Phone Settings me "AMPM Helpdesk" ko '
              '"Install unknown apps" allow karo, phir dobara "Install karo" dabao.';
        default:
          return 'Installer nahi khul paya (${r.message}).';
      }
    } catch (e) {
      return 'Installer nahi khul paya ($e).';
    }
  }
}
