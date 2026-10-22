import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:shared_preferences/shared_preferences.dart';
import '../models/ticket.dart';
import '../services/api_service.dart';
import '../app_theme.dart';
import 'raise_ticket_screen.dart';
import 'ticket_list_screen.dart';
import 'ticket_detail_screen.dart';
import 'setup_screen.dart';
import 'change_password_screen.dart';
import 'about_screen.dart';
import '../widgets/update_ui.dart';

class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key});
  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  List<Ticket> _tickets = [];
  List<Map<String, dynamic>> _assets = [];
  bool _loading = true;
  String? _error;
  String _empName = '', _empId = '', _empDept = '';

  // Remembers each ticket's status as of the last successful load, so the
  // 20-sec auto-refresh below can notice when the IT team changes one of
  // this employee's tickets (e.g. Open -> In Progress -> Resolved) and
  // alert them right in the app — without needing push notifications.
  Map<String, String> _lastStatusById = {};

  Timer? _autoRefresh;

  @override
  void initState() {
    super.initState();
    _loadProfile();
    _load();
    // Auto-refresh every 20 sec — picks up ticket updates from the IT team
    _autoRefresh = Timer.periodic(const Duration(seconds: 20), (_) => _load(silent: true));
  }

  @override
  void dispose() { _autoRefresh?.cancel(); super.dispose(); }

  Future<void> _loadProfile() async {
    final e = await ApiService.getSavedEmp();
    setState(() { _empName = e['name'] ?? ''; _empId = e['empId'] ?? ''; _empDept = e['dept'] ?? ''; });
  }

  Future<void> _load({bool silent = false}) async {
    if (!silent) setState(() { _loading = true; _error = null; });
    try {
      final t = await ApiService.getMyTickets();
      _notifyStatusChanges(t);
      setState(() { _tickets = t; _loading = false; });
      _lastStatusById = { for (final tk in t) tk.ticketId: tk.status };
      _loadAssets();
    } catch (e) {
      if (!silent) setState(() { _loading = false; _error = e.toString(); });
    }
  }

  // Assets assigned to this employee. A failure here (e.g. an older server without
  // the endpoint) must never break the tickets screen, so it is swallowed.
  Future<void> _loadAssets() async {
    try {
      final a = await ApiService.getMyAssets();
      if (mounted) setState(() => _assets = a);
    } catch (_) {}
  }

  IconData _assetIcon(String type) {
    final t = type.toLowerCase();
    if (t.contains('laptop')) return Icons.laptop_mac_rounded;
    if (t.contains('desktop') || t.contains('pc') || t.contains('cpu')) return Icons.desktop_windows_rounded;
    if (t.contains('phone') || t.contains('mobile') || t.contains('ipad') || t.contains('tablet')) return Icons.smartphone_rounded;
    if (t.contains('printer')) return Icons.print_rounded;
    if (t.contains('monitor') || t.contains('screen')) return Icons.monitor_rounded;
    return Icons.devices_other_rounded;
  }

  Widget _assetCard(Map<String, dynamic> a) {
    String v(String k) => (a[k] ?? '').toString().trim();
    final type = v('assetType');
    final brandModel = [v('brand'), v('model')].where((x) => x.isNotEmpty).join(' ');
    final serial = v('serial');
    final since = v('assignedDate');
    final cond = v('condition');
    final condColor = cond == 'Good' ? AppColors.green : cond == 'Fair' ? AppColors.amber : cond == 'Poor' ? AppColors.red : AppColors.textMuted;
    return Container(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: AppColors.cardBorder),
        boxShadow: AppShadows.tight,
      ),
      child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Container(
          width: 42, height: 42,
          decoration: BoxDecoration(color: AppColors.blue.withOpacity(.10), borderRadius: BorderRadius.circular(12)),
          child: Icon(_assetIcon(type), color: AppColors.blue, size: 22),
        ),
        const SizedBox(width: 12),
        Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Flexible(child: Text(v('assetTag'),
                style: const TextStyle(fontWeight: FontWeight.bold, color: AppColors.blue, fontSize: 13),
                overflow: TextOverflow.ellipsis)),
            if (type.isNotEmpty) ...[
              const SizedBox(width: 8),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                decoration: BoxDecoration(color: AppColors.bg, borderRadius: BorderRadius.circular(20)),
                child: Text(type, style: const TextStyle(color: AppColors.textMuted, fontSize: 10, fontWeight: FontWeight.w600)),
              ),
            ],
            const Spacer(),
            if (cond.isNotEmpty)
              Text(cond, style: TextStyle(color: condColor, fontSize: 10.5, fontWeight: FontWeight.bold)),
          ]),
          if (brandModel.isNotEmpty) ...[
            const SizedBox(height: 4),
            Text(brandModel, style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13, color: AppColors.textDark),
                maxLines: 1, overflow: TextOverflow.ellipsis),
          ],
          if (serial.isNotEmpty) ...[
            const SizedBox(height: 2),
            Text('S/N: $serial', style: const TextStyle(color: AppColors.textMuted, fontSize: 11)),
          ],
          if (since.isNotEmpty) ...[
            const SizedBox(height: 2),
            Text('Assigned: $since', style: const TextStyle(color: AppColors.textMuted, fontSize: 11)),
          ],
        ])),
      ]),
    );
  }

  // Compares the freshly-fetched tickets against what we saw last time and
  // alerts (sound + banner) for any that changed status since then. On the
  // very first load _lastStatusById is empty, so nothing fires — only real
  // changes noticed between refreshes trigger this.
  void _notifyStatusChanges(List<Ticket> fresh) {
    for (final t in fresh) {
      final prev = _lastStatusById[t.ticketId];
      if (prev != null && prev != t.status) {
        _notify('Ticket ${t.ticketNo} is now "${t.status}"');
      }
    }
  }

  void _notify(String message) {
    if (!mounted) return;
    SystemSound.play(SystemSoundType.alert);
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
      content: Text(message, style: const TextStyle(fontWeight: FontWeight.w600)),
      backgroundColor: AppColors.blue,
      behavior: SnackBarBehavior.floating,
      duration: const Duration(seconds: 4),
    ));
  }

  int get _open     => _tickets.where((t) => t.status == 'Open').length;
  int get _inProg   => _tickets.where((t) => t.status == 'In Progress').length;
  int get _resolved => _tickets.where((t) => t.status == 'Resolved' || t.status == 'Closed').length;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: AppColors.bg,
      appBar: AppBar(
        backgroundColor: AppColors.navy,
        title: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          const Text('AMPM IT Helpdesk',
              style: TextStyle(fontSize: 15, fontWeight: FontWeight.bold, color: Colors.white)),
          if (_empName.isNotEmpty)
            Text('$_empName  ·  $_empId', style: const TextStyle(fontSize: 11, color: AppColors.cyan)),
        ]),
        actions: [
          IconButton(icon: const Icon(Icons.refresh_rounded, color: Colors.white), onPressed: _load),
          PopupMenuButton<String>(
            icon: const Icon(Icons.more_vert_rounded, color: Colors.white),
            shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
            itemBuilder: (_) => [
              const PopupMenuItem(value: 'change_password',
                  child: Row(children: [Icon(Icons.key_rounded, size: 18), SizedBox(width: 8), Text('Change Password')])),
              const PopupMenuItem(value: 'about',
                  child: Row(children: [Icon(Icons.system_update_rounded, size: 18), SizedBox(width: 8), Text('App & Updates')])),
              const PopupMenuItem(value: 'logout',
                  child: Row(children: [Icon(Icons.logout_rounded, size: 18), SizedBox(width: 8), Text('Change Account')])),
            ],
            onSelected: (v) async {
              if (v == 'about') {
                Navigator.push(context, MaterialPageRoute(builder: (_) => const AboutScreen()));
                return;
              }
              if (v == 'change_password') {
                Navigator.push(context, MaterialPageRoute(builder: (_) => const ChangePasswordScreen(forced: false)));
                return;
              }
              final prefs = await SharedPreferences.getInstance();
              await prefs.remove('username');
              await prefs.remove('password');
              await prefs.remove('emp_id');
              await prefs.remove('emp_name');
              await prefs.remove('emp_dept');
              await prefs.remove('emp_desig');
              await prefs.remove('is_admin');
              if (context.mounted) Navigator.pushReplacement(context, MaterialPageRoute(builder: (_) => const SetupScreen()));
            },
          ),
        ],
      ),
      body: RefreshIndicator(
        color: AppColors.blue,
        onRefresh: _load,
        child: _loading ? const Center(child: CircularProgressIndicator(color: AppColors.blue))
            : _error != null ? _buildError() : _buildBody(),
      ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => Navigator.push(context, MaterialPageRoute(builder: (_) => const RaiseTicketScreen())).then((_) => _load()),
        backgroundColor: AppColors.green,
        elevation: 2,
        icon: const Icon(Icons.add_rounded, color: Colors.white),
        label: const Text('Raise Ticket', style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold)),
      ),
    );
  }

  Widget _buildError() => Center(child: Padding(padding: const EdgeInsets.all(24),
    child: Column(mainAxisAlignment: MainAxisAlignment.center, children: [
      Container(
        width: 72, height: 72,
        decoration: BoxDecoration(color: AppColors.red.withOpacity(.1), shape: BoxShape.circle),
        child: const Icon(Icons.wifi_off_rounded, size: 34, color: AppColors.red),
      ),
      const SizedBox(height: 18),
      const Text('Server se connect nahi hua', style: TextStyle(fontSize: 16, fontWeight: FontWeight.bold, color: AppColors.textDark)),
      const SizedBox(height: 8),
      Text(_error ?? '', style: const TextStyle(color: AppColors.textMuted, fontSize: 12), textAlign: TextAlign.center),
      const SizedBox(height: 24),
      ElevatedButton.icon(onPressed: _load, icon: const Icon(Icons.refresh_rounded), label: const Text('Retry'),
          style: ElevatedButton.styleFrom(backgroundColor: AppColors.blue, foregroundColor: Colors.white)),
      TextButton(onPressed: () => Navigator.pushReplacement(context, MaterialPageRoute(builder: (_) => const SetupScreen())),
          child: const Text('Settings change karo')),
    ])));

  Widget _buildBody() => ListView(padding: const EdgeInsets.all(16), children: [
    const UpdateBanner(),
    // Employee card
    Container(
      margin: const EdgeInsets.only(bottom: 16),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
          gradient: AppColors.navyGradient,
          borderRadius: BorderRadius.circular(20),
          boxShadow: AppShadows.soft),
      child: Row(children: [
        Container(
          width: 46, height: 46,
          decoration: BoxDecoration(color: Colors.white.withOpacity(.12), borderRadius: BorderRadius.circular(13)),
          child: const Icon(Icons.person_rounded, color: Colors.white),
        ),
        const SizedBox(width: 14),
        Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text(_empName, style: const TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 15)),
          const SizedBox(height: 2),
          Text('$_empId  ·  $_empDept', style: const TextStyle(color: AppColors.cyan, fontSize: 12)),
        ])),
        Container(padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
          decoration: BoxDecoration(color: Colors.white.withOpacity(.1), borderRadius: BorderRadius.circular(20)),
          child: Text('${_tickets.length} Tickets', style: const TextStyle(color: Colors.white, fontSize: 11, fontWeight: FontWeight.w600))),
      ]),
    ),
    // Stats
    Row(children: [
      StatTile(label: 'Open', value: _open, color: AppColors.red, icon: Icons.error_outline_rounded),
      const SizedBox(width: 10),
      StatTile(label: 'In Progress', value: _inProg, color: AppColors.amber, icon: Icons.autorenew_rounded),
      const SizedBox(width: 10),
      StatTile(label: 'Resolved', value: _resolved, color: AppColors.green, icon: Icons.check_circle_outline_rounded),
    ]),
    const SizedBox(height: 20),
    // Raise button
    GestureDetector(
      onTap: () => Navigator.push(context, MaterialPageRoute(builder: (_) => const RaiseTicketScreen())).then((_) => _load()),
      child: Container(
        padding: const EdgeInsets.all(18),
        decoration: BoxDecoration(
          gradient: const LinearGradient(colors: [AppColors.green, Color(0xFF10B981)],
              begin: Alignment.topLeft, end: Alignment.bottomRight),
          borderRadius: BorderRadius.circular(18),
          boxShadow: [BoxShadow(color: AppColors.green.withOpacity(.3), blurRadius: 16, offset: const Offset(0, 6))],
        ),
        child: Row(children: [
          Container(
            width: 44, height: 44,
            decoration: BoxDecoration(color: Colors.white.withOpacity(.18), borderRadius: BorderRadius.circular(12)),
            child: const Icon(Icons.add_circle_outline_rounded, color: Colors.white, size: 24),
          ),
          const SizedBox(width: 14),
          const Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text('IT Problem Report karo', style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 15)),
            SizedBox(height: 2),
            Text('Nayi ticket raise karo', style: TextStyle(color: Colors.white70, fontSize: 12)),
          ])),
          const Icon(Icons.arrow_forward_ios_rounded, color: Colors.white, size: 15),
        ]),
      ),
    ),
    const SizedBox(height: 22),
    // Assets assigned to this employee
    Row(children: [
      const Text('Mere Assets', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 16, color: AppColors.textDark)),
      const SizedBox(width: 8),
      Container(
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
        decoration: BoxDecoration(color: AppColors.blue.withOpacity(.10), borderRadius: BorderRadius.circular(20)),
        child: Text('${_assets.length}', style: const TextStyle(color: AppColors.blue, fontSize: 11, fontWeight: FontWeight.bold)),
      ),
    ]),
    const SizedBox(height: 10),
    if (_assets.isEmpty)
      Container(
        width: double.infinity,
        padding: const EdgeInsets.all(18),
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(16), border: Border.all(color: AppColors.cardBorder)),
        child: const Text('Abhi aapko koi asset assign nahi hai.',
            style: TextStyle(color: AppColors.textMuted, fontSize: 12.5)),
      )
    else
      ..._assets.map(_assetCard),
    const SizedBox(height: 22),
    Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
      const Text('Meri Tickets', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 16, color: AppColors.textDark)),
      TextButton(
        onPressed: () => Navigator.push(context, MaterialPageRoute(builder: (_) => const TicketListScreen())).then((_) => _load()),
        style: TextButton.styleFrom(foregroundColor: AppColors.blue),
        child: const Text('Sab dekho'),
      ),
    ]),
    const SizedBox(height: 4),
    if (_tickets.isEmpty)
      Container(
        padding: const EdgeInsets.symmetric(vertical: 40),
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(18), border: Border.all(color: AppColors.cardBorder)),
        child: const Column(children: [
          Icon(Icons.check_circle_outline_rounded, size: 44, color: AppColors.green),
          SizedBox(height: 10),
          Text('Koi open ticket nahi!', style: TextStyle(fontWeight: FontWeight.bold, color: AppColors.textDark)),
          SizedBox(height: 2),
          Text('Sab theek hai', style: TextStyle(color: AppColors.textMuted, fontSize: 12)),
        ]))
    else
      ..._tickets.take(10).map((t) => _ticketCard(t)),
    const SizedBox(height: 80),
  ]);

  Widget _ticketCard(Ticket t) => GestureDetector(
    onTap: () => Navigator.push(context, MaterialPageRoute(builder: (_) => TicketDetailScreen(ticket: t))).then((_) => _load()),
    child: Container(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(16),
        border: Border(left: BorderSide(color: t.statusColor, width: 4)),
        boxShadow: AppShadows.tight,
      ),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(children: [
          Text(t.ticketNo, style: const TextStyle(fontWeight: FontWeight.bold, color: AppColors.blue, fontSize: 12)),
          const SizedBox(width: 8),
          Container(padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
            decoration: BoxDecoration(color: t.statusColor.withOpacity(.1), borderRadius: BorderRadius.circular(20)),
            child: Text(t.status, style: TextStyle(color: t.statusColor, fontSize: 10, fontWeight: FontWeight.bold))),
          const Spacer(),
          Text(t.dateRaised.length > 10 ? t.dateRaised.substring(0, 10) : t.dateRaised,
              style: const TextStyle(color: AppColors.textMuted, fontSize: 10)),
        ]),
        const SizedBox(height: 6),
        Text(t.title, style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13.5, color: AppColors.textDark),
            maxLines: 1, overflow: TextOverflow.ellipsis),
        const SizedBox(height: 3),
        Text('${t.issueType}  ·  ${t.priority}', style: const TextStyle(color: AppColors.textMuted, fontSize: 11)),
        if (t.expectedResolution.isNotEmpty && t.status != 'Resolved' && t.status != 'Closed') ...[
          const SizedBox(height: 3),
          Text('⏱ ETA: ${t.expectedResolution}',
              style: const TextStyle(color: AppColors.amber, fontSize: 10.5, fontWeight: FontWeight.w600)),
        ],
        if ((t.status == 'Resolved' || t.status == 'Closed') && t.empFeedbackStatus.isEmpty) ...[
          const SizedBox(height: 3),
          const Text('Feedback pending — apna feedback do',
              style: TextStyle(color: AppColors.red, fontSize: 10.5, fontWeight: FontWeight.w600)),
        ],
      ]),
    ),
  );
}
