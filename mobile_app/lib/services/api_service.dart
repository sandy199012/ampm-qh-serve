import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'package:http/http.dart' as http;
import 'package:shared_preferences/shared_preferences.dart';
import '../models/ticket.dart';

class ApiService {
  // Phase 2: tickets now sync to the live AMPM website (Render + Supabase)
  // instead of Sandy's PC on the office LAN — so the app works from anywhere,
  // not just when the phone is on the same Wi-Fi as his desktop.
  static const String _baseUrl = 'https://ampm-qh-serve-1.onrender.com';

  static Future<String> getBaseUrl() async => _baseUrl;

  // Every request carries the logged-in user's username+password via HTTP
  // Basic Auth — the server (AMPMWeb ApiController) verifies it fresh
  // against the users table on every call. No token/session to manage.
  static Future<Map<String, String>> _authHeaders({bool json = false}) async {
    final prefs = await SharedPreferences.getInstance();
    final username = prefs.getString('username') ?? '';
    final password = prefs.getString('password') ?? '';
    final token = base64Encode(utf8.encode('$username:$password'));
    final headers = {'Authorization': 'Basic $token'};
    if (json) headers['Content-Type'] = 'application/json';
    return headers;
  }

  // ══════════════════════════════════════════════════════════════
  //  LAN auto-discovery — fixes "the server's IP keeps changing"
  //  Sandy's PC gets its LAN IP from DHCP, so it can change any time the
  //  router reassigns it or the PC reconnects to Wi-Fi. Instead of the
  //  app relying on a hardcoded/manually-typed IP, it broadcasts a UDP
  //  packet on the LAN; the Helpdesk server (see Program.cs) listens on
  //  UDP port 47810 and replies from whatever IP it's actually running
  //  on right now. Every request helper below (_get/_postRaw/_deleteRaw)
  //  auto-triggers this and saves the fresh IP the moment a call fails
  //  to connect — so the app self-heals without the user retyping
  //  anything, and Setup screen also gets a manual "Auto-Detect" button.
  // ══════════════════════════════════════════════════════════════
  static const int _discoveryPort = 47810;
  static const String _discoveryMsg = 'AMPM_HELPDESK_DISCOVER';
  static bool _discovering = false;

  static bool _isConnError(Object e) =>
      e is SocketException || e is TimeoutException || e is HttpException ||
      e is http.ClientException;

  /// Broadcasts on the LAN and waits briefly for the Helpdesk server to
  /// answer with its current IP/port. Returns null if nothing answers
  /// (server off, different network, firewall blocking UDP, etc).
  static Future<String?> discoverServer() async {
    if (_discovering) {
      await Future.delayed(const Duration(milliseconds: 800));
      return null;
    }
    _discovering = true;
    RawDatagramSocket? socket;
    StreamSubscription? sub;
    try {
      socket = await RawDatagramSocket.bind(InternetAddress.anyIPv4, 0);
      socket.broadcastEnabled = true;
      socket.send(utf8.encode(_discoveryMsg), InternetAddress('255.255.255.255'), _discoveryPort);

      final completer = Completer<String?>();
      sub = socket.listen((event) {
        if (event != RawSocketEvent.read) return;
        final dg = socket!.receive();
        if (dg == null) return;
        try {
          final j = jsonDecode(utf8.decode(dg.data)) as Map<String, dynamic>;
          if (j['service'] == 'AMPM_HELPDESK' && !completer.isCompleted) {
            final port = j['port']?.toString() ?? '5000';
            completer.complete('http://${dg.address.address}:$port');
          }
        } catch (_) {}
      });
      return await completer.future.timeout(const Duration(seconds: 3), onTimeout: () => null);
    } catch (_) {
      return null;
    } finally {
      await sub?.cancel();
      socket?.close();
      _discovering = false;
    }
  }

  /// Same as discoverServer(), but also saves the result as the active
  /// server_url so it's picked up immediately and on next app launch.
  static Future<String?> discoverAndSave() async {
    final found = await discoverServer();
    if (found != null) {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setString('server_url', found);
    }
    return found;
  }

  static Future<http.Response> _get(String path, {Duration timeout = const Duration(seconds: 15)}) async {
    final headers = await _authHeaders();
    return await http.get(Uri.parse('$_baseUrl$path'), headers: headers).timeout(timeout);
  }

  static Future<http.Response> _postRaw(String path, Object body,
      {Duration timeout = const Duration(seconds: 20)}) async {
    final headers = await _authHeaders(json: true);
    return await http.post(Uri.parse('$_baseUrl$path'), headers: headers, body: body).timeout(timeout);
  }

  static Future<http.Response> _deleteRaw(String path, {Duration timeout = const Duration(seconds: 15)}) async {
    final headers = await _authHeaders();
    return await http.delete(Uri.parse('$_baseUrl$path'), headers: headers).timeout(timeout);
  }

  static Future<Map<String,String>> getSavedEmp() async {
    final prefs = await SharedPreferences.getInstance();
    return {
      'empId': prefs.getString('emp_id')    ?? '',
      'name':  prefs.getString('emp_name')  ?? '',
      'dept':  prefs.getString('emp_dept')  ?? '',
      'desig': prefs.getString('emp_desig') ?? '',
    };
  }

  // Only this employee's tickets — the server scopes this automatically by
  // the logged-in account's linked Employee Code, so no empId param needed.
  static Future<List<Ticket>> getMyTickets() async {
    final res = await _get('/api/tickets', timeout: const Duration(seconds: 15));
    if (res.statusCode == 200) {
      final List data = jsonDecode(res.body);
      return data.map((j) => Ticket.fromJson(j)).toList();
    }
    throw Exception('Failed: ${res.statusCode}');
  }

  // Admin: every employee's tickets (same endpoint — server returns all for admins)
  static Future<List<Ticket>> getAllTickets() async {
    final res = await _get('/api/tickets', timeout: const Duration(seconds: 15));
    if (res.statusCode == 200) {
      final List data = jsonDecode(res.body);
      return data.map((j) => Ticket.fromJson(j)).toList();
    }
    throw Exception('Failed: ${res.statusCode}');
  }

  // Admin: full employee directory (no password/bank fields — server whitelists)
  static Future<List<Map<String,dynamic>>> getEmployeesFull() async {
    final res = await _get('/api/employees/full', timeout: const Duration(seconds: 10));
    if (res.statusCode == 200) {
      final List data = jsonDecode(res.body);
      return data.cast<Map<String,dynamic>>();
    }
    throw Exception('Failed: ${res.statusCode}');
  }

  static Future<Ticket> getTicket(String id) async {
    final res = await _get('/api/tickets/$id', timeout: const Duration(seconds: 10));
    if (res.statusCode == 200) return Ticket.fromJson(jsonDecode(res.body));
    throw Exception('Ticket not found');
  }

  static Future<Ticket> raiseTicket(Map<String, dynamic> data) async {
    final prefs = await SharedPreferences.getInstance();
    data['empId']    = prefs.getString('emp_id')    ?? '';
    data['empName']  = prefs.getString('emp_name')  ?? '';
    data['empDept']  = prefs.getString('emp_dept')  ?? '';
    data['empDesig'] = prefs.getString('emp_desig') ?? '';
    final res = await _postRaw('/api/tickets', jsonEncode(data), timeout: const Duration(seconds: 15));
    if (res.statusCode == 200 || res.statusCode == 201)
      return Ticket.fromJson(jsonDecode(res.body));
    throw Exception('Failed: ${res.body}');
  }

  static Future<void> updateTicket(String id, Map<String, dynamic> data) async {
    await _postRaw('/api/tickets/$id/update', jsonEncode(data), timeout: const Duration(seconds: 10));
  }

  // Employee's confirmation once a ticket is Resolved/Closed — mirrors the
  // web "My Helpdesk" feedback form and the same server-side validation
  // (feedbackStatus must be 'Resolved' or 'Not Resolved', ticket must
  // already be Resolved/Closed). Throws with the server's error message.
  static Future<void> submitFeedback(String id, String feedbackStatus, String feedbackComment) async {
    final res = await _postRaw('/api/tickets/$id/feedback', jsonEncode({
      'feedbackStatus': feedbackStatus,
      'feedbackComment': feedbackComment,
    }), timeout: const Duration(seconds: 12));
    if (res.statusCode != 200) throw Exception('Failed: ${res.statusCode}');
    final j = jsonDecode(res.body);
    if (j['ok'] != true) throw Exception(j['error']?.toString() ?? 'Failed to submit feedback');
  }

  static Future<void> deleteTicket(String id) async {
    final res = await _deleteRaw('/api/tickets/$id', timeout: const Duration(seconds: 10));
    if (res.statusCode != 200) throw Exception('Failed: ${res.statusCode}');
  }

  static Future<List<Map<String,String>>> getEmployeeList(String baseUrl) async {
    try {
      final res = await http.get(Uri.parse('$baseUrl/api/employees'))
          .timeout(const Duration(seconds: 10));
      if (res.statusCode == 200) {
        final List data = jsonDecode(res.body);
        return data.map<Map<String,String>>((e) => {
          'empId': e['empId']?.toString() ?? '',
          'name':  e['name']?.toString()  ?? '',
          'dept':  e['dept']?.toString()  ?? '',
          'desig': e['desig']?.toString() ?? '',
        }).toList();
      }
    } catch (_) {}
    return [];
  }

  static Future<List<String>> getDepartments() async {
    try {
      final res = await _get('/api/departments', timeout: const Duration(seconds: 8));
      if (res.statusCode == 200) {
        final List data = jsonDecode(res.body);
        return data.cast<String>();
      }
    } catch (_) {}
    return ['IT','HR','Finance','Design','Production','Admin','Retail','Other'];
  }

  // Live-website login: username+password are the AMPMWeb login account.
  // For most employees the username IS their Employee Code (mobile logins
  // are bulk-provisioned that way — see Employees > Provision Mobile Logins),
  // so this doubles as "log in with Employee Code + Password".
  static Future<Map<String,String>?> login(String username, String password) async {
    try {
      final res = await http.post(Uri.parse('$_baseUrl/api/login'),
          headers: {'Content-Type': 'application/json'},
          body: jsonEncode({'username': username, 'password': password}))
          .timeout(const Duration(seconds: 15));
      if (res.statusCode == 200) {
        final j = jsonDecode(res.body);
        if (j['ok'] != true) return null;
        return {
          'username': j['username']?.toString() ?? username,
          'empId': j['empId']?.toString() ?? '',
          'name':  j['name']?.toString()  ?? '',
          'dept':  j['dept']?.toString()  ?? '',
          'desig': j['desig']?.toString() ?? '',
          'isAdmin': (j['isAdmin'] == true) ? 'true' : 'false',
          'mustChangePassword': (j['mustChangePassword'] == true) ? 'true' : 'false',
        };
      }
    } catch (_) {}
    return null;
  }

  // Changes the logged-in user's password (used after a forced first-login
  // change, or any time from Settings). Uses the CURRENT saved credentials
  // for Basic Auth, so call this before overwriting the saved password.
  static Future<bool> changePassword(String newPassword) async {
    try {
      final res = await _postRaw('/api/change-password', jsonEncode({'newPassword': newPassword}));
      if (res.statusCode != 200) return false;
      final j = jsonDecode(res.body);
      return j['ok'] == true;
    } catch (_) {
      return false;
    }
  }

  static Future<bool> getSavedIsAdmin() async {
    final prefs = await SharedPreferences.getInstance();
    return prefs.getBool('is_admin') ?? false;
  }

  static Future<bool> testConnection() async {
    try {
      final res = await http.get(Uri.parse('$_baseUrl/api/ping'))
          .timeout(const Duration(seconds: 10));
      return res.statusCode == 200;
    } catch (_) { return false; }
  }

  // Throws Exception(message-from-server) on non-2xx, else returns decoded body.
  static Future<dynamic> _postJson(String path, Map<String, dynamic> body) async {
    final res = await _postRaw(path, jsonEncode(body));
    if (res.statusCode >= 200 && res.statusCode < 300) return jsonDecode(res.body);
    try {
      final j = jsonDecode(res.body);
      throw Exception(j['message']?.toString() ?? 'Failed (${res.statusCode})');
    } catch (_) { throw Exception('Failed (${res.statusCode})'); }
  }

  static Future<List<Map<String, dynamic>>> _getList(String path) async {
    final res = await _get(path);
    if (res.statusCode == 200) {
      final List data = jsonDecode(res.body);
      return data.cast<Map<String, dynamic>>();
    }
    throw Exception('Failed: ${res.statusCode}');
  }

  // The logged-in employee's own assigned assets (shown on the home screen).
  static Future<List<Map<String, dynamic>>> getMyAssets() => _getList('/api/my-assets');

  // ── Asset Stock ────────────────────────────────────────────
  static Future<List<Map<String, dynamic>>> getAssets({String? status}) async {
    final q = (status != null && status != 'All') ? '?status=${Uri.encodeComponent(status)}' : '';
    return _getList('/api/assets$q');
  }

  static Future<Map<String, dynamic>> saveAsset(Map<String, dynamic> data) async =>
      await _postJson('/api/assets', data);

  static Future<Map<String, dynamic>> assignAsset(String id, String empId) async =>
      await _postJson('/api/assets/$id/assign', {'empId': empId});

  static Future<Map<String, dynamic>> unassignAsset(String id) async =>
      await _postJson('/api/assets/$id/unassign', {});

  // ── IT Store ───────────────────────────────────────────────
  static Future<List<Map<String, dynamic>>> getStoreItems() => _getList('/api/store/items');

  static Future<Map<String, dynamic>> saveStoreItem(Map<String, dynamic> item,
      {int openingQty = 0, double openingRate = 0}) async =>
      await _postJson('/api/store/items', {'item': item, 'openingQty': openingQty, 'openingRate': openingRate});

  static Future<Map<String, dynamic>> stockInStoreItem(String id, Map<String, dynamic> data) async =>
      await _postJson('/api/store/items/$id/stockin', data);

  static Future<Map<String, dynamic>> issueStoreItem(Map<String, dynamic> data) async =>
      await _postJson('/api/store/issue', data);

  static Future<Map<String, dynamic>> returnStoreIssue(String id, Map<String, dynamic> data) async =>
      await _postJson('/api/store/issues/$id/return', data);

  static Future<List<Map<String, dynamic>>> getStoreIssues({String? itemId}) async {
    final q = itemId != null ? '?itemId=${Uri.encodeComponent(itemId)}' : '';
    return _getList('/api/store/issues$q');
  }

  // ── Peripheral Stock ───────────────────────────────────────
  static Future<List<Map<String, dynamic>>> getPeripheralItems() => _getList('/api/peripherals/items');

  static Future<Map<String, dynamic>> savePeripheralItem(Map<String, dynamic> item,
      {int openingQty = 0, double openingRate = 0}) async =>
      await _postJson('/api/peripherals/items', {'item': item, 'openingQty': openingQty, 'openingRate': openingRate});

  static Future<Map<String, dynamic>> stockInPeripheral(String id, Map<String, dynamic> data) async =>
      await _postJson('/api/peripherals/items/$id/stockin', data);

  static Future<Map<String, dynamic>> issuePeripheral(Map<String, dynamic> data) async =>
      await _postJson('/api/peripherals/issue', data);

  static Future<Map<String, dynamic>> returnPeripheralIssue(String id, Map<String, dynamic> data) async =>
      await _postJson('/api/peripherals/issues/$id/return', data);

  static Future<List<Map<String, dynamic>>> getPeripheralIssues({String? itemId}) async {
    final q = itemId != null ? '?itemId=${Uri.encodeComponent(itemId)}' : '';
    return _getList('/api/peripherals/issues$q');
  }

  static Future<dynamic> _deleteJson(String path) async {
    final res = await _deleteRaw(path);
    if (res.statusCode >= 200 && res.statusCode < 300) return jsonDecode(res.body);
    try {
      final j = jsonDecode(res.body);
      throw Exception(j['message']?.toString() ?? 'Failed (${res.statusCode})');
    } catch (_) { throw Exception('Failed (${res.statusCode})'); }
  }

  // ── Cartridges ─────────────────────────────────────────────
  static Future<List<Map<String, dynamic>>> getCartridges() => _getList('/api/cartridges');

  static Future<Map<String, dynamic>> saveCartridge(Map<String, dynamic> data) async =>
      await _postJson('/api/cartridges', data);

  static Future<void> deleteCartridge(String id) async => await _deleteJson('/api/cartridges/$id');

  static Future<List<Map<String, dynamic>>> getCartridgeIssues({String? dept}) async {
    final q = (dept != null && dept != 'All Departments') ? '?dept=${Uri.encodeComponent(dept)}' : '';
    return _getList('/api/cartridges/issues$q');
  }

  static Future<Map<String, dynamic>> issueCartridge(Map<String, dynamic> data) async =>
      await _postJson('/api/cartridges/issue', data);

  static Future<void> deleteCartridgeIssue(String id) async => await _deleteJson('/api/cartridges/issues/$id');

  // ── Printable issue forms (opened in the phone's browser) ───
  static Future<String> printUrl(String kind, String issueId) async {
    final base = await getBaseUrl();
    return '$base/print/$kind/$issueId';
  }

  // ── Purchase Orders ────────────────────────────────────────
  static Future<List<Map<String, dynamic>>> getPOs({String? status}) async {
    final q = (status != null && status != 'All') ? '?status=${Uri.encodeComponent(status)}' : '';
    return _getList('/api/pos$q');
  }

  static Future<String> nextPoNumber() async {
    final res = await _get('/api/pos/next-number', timeout: const Duration(seconds: 10));
    if (res.statusCode == 200) return jsonDecode(res.body)['poNumber']?.toString() ?? '';
    return '';
  }

  static Future<Map<String, dynamic>> savePO(Map<String, dynamic> data) async =>
      await _postJson('/api/pos', data);

  static Future<void> deletePO(String poNumber) async =>
      await _deleteJson('/api/pos/${Uri.encodeComponent(poNumber)}');

  // ── Vendors ────────────────────────────────────────────────
  static Future<List<Map<String, dynamic>>> getVendors() => _getList('/api/vendors');

  static Future<Map<String, dynamic>> saveVendor(Map<String, dynamic> data) async =>
      await _postJson('/api/vendors', data);

  static Future<void> deleteVendor(String id) async => await _deleteJson('/api/vendors/$id');

  // ── Asset / IT Purchases ──────────────────────────────────
  static Future<List<Map<String, dynamic>>> getPurchases() => _getList('/api/purchases');

  static Future<Map<String, dynamic>> savePurchase(Map<String, dynamic> data) async =>
      await _postJson('/api/purchases', data);

  static Future<void> deletePurchase(String id) async => await _deleteJson('/api/purchases/$id');

  // ── Purchase Bills ─────────────────────────────────────────
  static Future<List<Map<String, dynamic>>> getPurchaseBills() => _getList('/api/purchase-bills');

  static Future<Map<String, dynamic>> savePurchaseBill(Map<String, dynamic> data) async =>
      await _postJson('/api/purchase-bills', data);

  static Future<void> deletePurchaseBill(String id) async => await _deleteJson('/api/purchase-bills/$id');

  // ── Recurring Bills ────────────────────────────────────────
  static Future<List<Map<String, dynamic>>> getBills() => _getList('/api/bills');

  static Future<Map<String, dynamic>> saveBill(Map<String, dynamic> data) async =>
      await _postJson('/api/bills', data);

  static Future<void> deleteBill(String id) async => await _deleteJson('/api/bills/$id');

  // ── Budget ─────────────────────────────────────────────────
  static Future<List<Map<String, dynamic>>> getBudget() => _getList('/api/budget');

  static Future<Map<String, dynamic>> saveBudgetItem(Map<String, dynamic> data) async =>
      await _postJson('/api/budget', data);

  static Future<void> deleteBudgetItem(String id) async => await _deleteJson('/api/budget/$id');

  // ── Licenses ───────────────────────────────────────────────
  static Future<List<Map<String, dynamic>>> getLicenses() => _getList('/api/licenses');

  static Future<Map<String, dynamic>> saveLicense(Map<String, dynamic> data) async =>
      await _postJson('/api/licenses', data);

  static Future<void> deleteLicense(String id) async => await _deleteJson('/api/licenses/$id');

  // ── IT Goals ───────────────────────────────────────────────
  static Future<List<Map<String, dynamic>>> getGoals({int? week}) async {
    final q = week != null ? '?week=$week' : '';
    return _getList('/api/goals$q');
  }

  static Future<Map<String, dynamic>> saveGoal(Map<String, dynamic> data) async =>
      await _postJson('/api/goals', data);

  static Future<void> deleteGoal(String id) async => await _deleteJson('/api/goals/$id');

  // Current tracked week (server-side rule, same as AMPMTool desktop) +
  // this calendar week's Monday-Sunday bounds — call before opening the
  // "Add Goal" form so weekNo/weekStart/weekEnd are always set correctly
  // (a goal saved without weekEnd breaks server-side auto-carry-forward).
  static Future<Map<String, dynamic>> getCurrentWeekInfo() async {
    final res = await _get('/api/goals/current-week', timeout: const Duration(seconds: 10));
    if (res.statusCode == 200) return jsonDecode(res.body) as Map<String, dynamic>;
    throw Exception('Failed: ${res.statusCode}');
  }

  // Manual "move incomplete goals to next week" — mirrors the desktop's
  // carry-forward button. Omit weekNo to carry the latest tracked week.
  static Future<Map<String, dynamic>> carryForwardGoals({int? weekNo}) async =>
      await _postJson('/api/goals/carry-forward', weekNo != null ? {'weekNo': weekNo} : {});

  // ── Dashboard Alerts ───────────────────────────────────────
  static Future<List<Map<String, dynamic>>> getAlerts() => _getList('/api/alerts');
}
