import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:shared_preferences/shared_preferences.dart';
import '../models/ticket.dart';
import '../services/api_service.dart';
import 'ticket_list_screen.dart';
import 'ticket_detail_screen.dart';
import 'employee_directory_screen.dart';
import 'setup_screen.dart';
import 'web_portal_screen.dart';
import 'change_password_screen.dart';
import 'about_screen.dart';
import '../widgets/update_ui.dart';

class AdminHomeScreen extends StatefulWidget {
  const AdminHomeScreen({super.key});
  @override
  State<AdminHomeScreen> createState() => _AdminHomeScreenState();
}

class _AdminHomeScreenState extends State<AdminHomeScreen> {
  List<Ticket> _tickets = [];
  bool _loading = true;
  String? _error;

  // Ticket IDs already seen as of the last successful load — lets the 20-sec
  // auto-refresh below notice a ticket an employee just raised (from mobile,
  // web, or desktop) and alert Super Admin/IT right in the app, the same way
  // the web Helpdesk page's sound alert does.
  Set<String> _knownTicketIds = {};

  Timer? _autoRefresh;

  @override
  void initState() {
    super.initState();
    _load();
    // Auto-refresh every 20 sec — picks up tickets raised from mobile/web/desktop
    _autoRefresh = Timer.periodic(const Duration(seconds: 20), (_) => _load(silent: true));
  }

  @override
  void dispose() { _autoRefresh?.cancel(); super.dispose(); }

  Future<void> _load({bool silent = false}) async {
    if (!silent) setState(() { _loading = true; _error = null; });
    try {
      final t = await ApiService.getAllTickets();
      _notifyNewTickets(t);
      setState(() { _tickets = t; _loading = false; });
      _knownTicketIds = t.map((tk) => tk.ticketId).toSet();
    } catch (e) {
      if (!silent) setState(() { _loading = false; _error = e.toString(); });
    }
  }

  // Alerts (sound + banner) for any ticket that wasn't in the previous load.
  // Skipped entirely on the very first load (_knownTicketIds still empty) —
  // otherwise every ticket already in the system would look "new" the moment
  // an admin opens the app.
  void _notifyNewTickets(List<Ticket> fresh) {
    if (_knownTicketIds.isEmpty) return;
    for (final t in fresh) {
      if (!_knownTicketIds.contains(t.ticketId)) {
        _notify('New ticket ${t.ticketNo} — ${t.title} (${t.empName})');
      }
    }
  }

  void _notify(String message) {
    if (!mounted) return;
    SystemSound.play(SystemSoundType.alert);
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
      content: Text(message, style: const TextStyle(fontWeight: FontWeight.w600)),
      backgroundColor: const Color(0xFFDC2626),
      behavior: SnackBarBehavior.floating,
      duration: const Duration(seconds: 5),
    ));
  }

  int get _open     => _tickets.where((t) => t.status == 'Open').length;
  int get _inProg   => _tickets.where((t) => t.status == 'In Progress' || t.status == 'Acknowledged').length;
  int get _resolved => _tickets.where((t) => t.status == 'Resolved' || t.status == 'Closed').length;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFFF0F4F8),
      appBar: AppBar(
        backgroundColor: const Color(0xFF0A192F),
        title: const Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text('AMPM IT Helpdesk', style: TextStyle(fontSize: 15, fontWeight: FontWeight.bold, color: Colors.white)),
          Text('Admin Dashboard', style: TextStyle(fontSize: 11, color: Color(0xFF06B6D4))),
        ]),
        actions: [
          IconButton(icon: const Icon(Icons.refresh, color: Colors.white), onPressed: _load),
          PopupMenuButton<String>(
            icon: const Icon(Icons.more_vert, color: Colors.white),
            itemBuilder: (_) => [
              const PopupMenuItem(value: 'change_password',
                  child: Row(children: [Icon(Icons.key_rounded, size: 18), SizedBox(width: 8), Text('Change Password')])),
              const PopupMenuItem(value: 'about',
                  child: Row(children: [Icon(Icons.system_update_rounded, size: 18), SizedBox(width: 8), Text('App & Updates')])),
              const PopupMenuItem(value: 'logout',
                  child: Row(children: [Icon(Icons.logout, size: 18), SizedBox(width: 8), Text('Logout')])),
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
      body: RefreshIndicator(onRefresh: _load,
        child: _loading ? const Center(child: CircularProgressIndicator())
            : _error != null ? _buildError() : _buildBody()),
    );
  }

  Widget _buildError() => Center(child: Padding(padding: const EdgeInsets.all(24),
    child: Column(mainAxisAlignment: MainAxisAlignment.center, children: [
      const Icon(Icons.wifi_off, size: 64, color: Color(0xFFDC2626)),
      const SizedBox(height: 16),
      const Text('Server se connect nahi hua', style: TextStyle(fontSize: 16, fontWeight: FontWeight.bold)),
      const SizedBox(height: 8),
      Text(_error ?? '', style: const TextStyle(color: Colors.grey, fontSize: 12), textAlign: TextAlign.center),
      const SizedBox(height: 24),
      ElevatedButton.icon(onPressed: _load, icon: const Icon(Icons.refresh), label: const Text('Retry'),
          style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF2563EB), foregroundColor: Colors.white)),
    ])));

  Widget _buildBody() => ListView(padding: const EdgeInsets.all(16), children: [
    const UpdateBanner(),
    Container(margin: const EdgeInsets.only(bottom: 16), padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
          gradient: const LinearGradient(colors: [Color(0xFF0A192F), Color(0xFF153566)]),
          borderRadius: BorderRadius.circular(14)),
      child: Row(children: [
        const CircleAvatar(backgroundColor: Color(0xFF7C3AED), child: Icon(Icons.admin_panel_settings, color: Colors.white)),
        const SizedBox(width: 12),
        Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text('IT Admin', style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 15)),
          Text('Sabhi tickets aur employees', style: TextStyle(color: Color(0xFF06B6D4), fontSize: 12)),
        ])),
        Container(padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
          decoration: BoxDecoration(color: Colors.white12, borderRadius: BorderRadius.circular(20)),
          child: Text('${_tickets.length} Tickets', style: const TextStyle(color: Colors.white, fontSize: 11))),
      ]),
    ),
    Row(children: [
      _stat('Open', _open, const Color(0xFFDC2626)),
      const SizedBox(width: 8),
      _stat('In Progress', _inProg, const Color(0xFFD97706)),
      const SizedBox(width: 8),
      _stat('Resolved', _resolved, const Color(0xFF059669)),
    ]),
    const SizedBox(height: 20),

    // Superadmin ke liye poori website — bina APK dobara build kiye Assets,
    // IT Store, Purchase Orders, Vendors, Budget, Bills, Licenses, Goals,
    // Users, Employees — sab kuch, seedha yahin se.
    GestureDetector(
      onTap: () => Navigator.push(context, MaterialPageRoute(
          builder: (_) => const WebPortalScreen(path: '/', title: 'AMPM IT Tool — Poori Website'))),
      child: Container(padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(
            gradient: const LinearGradient(colors: [Color(0xFF7C3AED), Color(0xFF2563EB)]),
            borderRadius: BorderRadius.circular(14),
            boxShadow: [BoxShadow(color: const Color(0xFF7C3AED).withOpacity(.3), blurRadius: 10, offset: const Offset(0, 4))]),
        child: const Row(children: [
          Icon(Icons.public, color: Colors.white, size: 28),
          SizedBox(width: 12),
          Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text('Full Website — Sabhi Modules', style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 15)),
            Text('Assets, IT Store, PO, Vendors, Budget, Bills, Licenses, Goals, Users — sab yahan',
                style: TextStyle(color: Colors.white70, fontSize: 11)),
          ])),
          Icon(Icons.arrow_forward_ios, color: Colors.white, size: 16),
        ])),
    ),
    const SizedBox(height: 12),

    _menuCard(
      icon: Icons.confirmation_number,
      color: const Color(0xFF2563EB),
      title: 'Sabhi Tickets',
      subtitle: 'View, assign, update, delete — sabhi employees ki tickets',
      onTap: () => Navigator.push(context,
          MaterialPageRoute(builder: (_) => const TicketListScreen(all: true))).then((_) => _load()),
    ),
    const SizedBox(height: 12),
    _menuCard(
      icon: Icons.people_alt,
      color: const Color(0xFF059669),
      title: 'Employee Directory',
      subtitle: 'Search karo aur call karo — sabhi employees',
      onTap: () => Navigator.push(context,
          MaterialPageRoute(builder: (_) => const EmployeeDirectoryScreen())),
    ),

    const SizedBox(height: 20),
    Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(color: const Color(0xFFEFF6FF), borderRadius: BorderRadius.circular(12),
          border: Border.all(color: const Color(0xFFBFDBFE))),
      child: const Row(children: [
        Icon(Icons.info_outline, color: Color(0xFF2563EB), size: 20),
        SizedBox(width: 10),
        Expanded(child: Text(
          'Baaki modules (Assets, IT Store, Purchase Orders, Vendors, Budget, Bills, Licenses, IT Goals) upar "Full Website" button se seedha khulte hain — aage website mein koi naya feature aaye to bhi APK dobara install karne ki zarurat nahi.',
          style: TextStyle(fontSize: 12, color: Color(0xFF1E3A8A)))),
      ]),
    ),

    const SizedBox(height: 20),
    if (_tickets.isNotEmpty) ...[
      Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
        const Text('Recent Tickets', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 15, color: Color(0xFF0A192F))),
        TextButton(onPressed: () => Navigator.push(context,
            MaterialPageRoute(builder: (_) => const TicketListScreen(all: true))).then((_) => _load()),
            child: const Text('Sab dekho')),
      ]),
      ..._tickets.take(8).map((t) => _ticketCard(t)),
    ],
    const SizedBox(height: 32),
  ]);

  Widget _stat(String l, int v, Color c) => Expanded(child: Container(
    padding: const EdgeInsets.symmetric(vertical: 14, horizontal: 8),
    decoration: BoxDecoration(gradient: LinearGradient(colors: [c.withOpacity(.8), c],
        begin: Alignment.topLeft, end: Alignment.bottomRight), borderRadius: BorderRadius.circular(12)),
    child: Column(children: [
      Text(v.toString(), style: const TextStyle(color: Colors.white, fontSize: 22, fontWeight: FontWeight.bold)),
      Text(l, style: const TextStyle(color: Colors.white70, fontSize: 10), textAlign: TextAlign.center),
    ]),
  ));

  Widget _menuCard({required IconData icon, required Color color, required String title,
      required String subtitle, required VoidCallback onTap}) => GestureDetector(
    onTap: onTap,
    child: Container(padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(14),
          boxShadow: [BoxShadow(color: Colors.black.withOpacity(.04), blurRadius: 8)]),
      child: Row(children: [
        Container(width: 46, height: 46,
          decoration: BoxDecoration(color: color.withOpacity(.12), borderRadius: BorderRadius.circular(12)),
          child: Icon(icon, color: color)),
        const SizedBox(width: 14),
        Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text(title, style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 14)),
          const SizedBox(height: 2),
          Text(subtitle, style: const TextStyle(color: Colors.grey, fontSize: 11)),
        ])),
        const Icon(Icons.arrow_forward_ios, size: 14, color: Colors.grey),
      ]),
    ),
  );

  Widget _ticketCard(Ticket t) => GestureDetector(
    onTap: () => Navigator.push(context,
        MaterialPageRoute(builder: (_) => TicketDetailScreen(ticket: t))).then((_) => _load()),
    child: Container(margin: const EdgeInsets.only(top: 10), padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12),
          border: Border(left: BorderSide(color: t.statusColor, width: 4)),
          boxShadow: [BoxShadow(color: Colors.black.withOpacity(.04), blurRadius: 6)]),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(children: [
          Text(t.ticketNo, style: const TextStyle(fontWeight: FontWeight.bold, color: Color(0xFF2563EB), fontSize: 12)),
          const SizedBox(width: 8),
          Container(padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
            decoration: BoxDecoration(color: t.statusColor.withOpacity(.1), borderRadius: BorderRadius.circular(20)),
            child: Text(t.status, style: TextStyle(color: t.statusColor, fontSize: 10, fontWeight: FontWeight.bold))),
          const Spacer(),
          Text(t.dateRaised.length > 10 ? t.dateRaised.substring(0, 10) : t.dateRaised,
              style: const TextStyle(color: Colors.grey, fontSize: 10)),
        ]),
        const SizedBox(height: 4),
        Text(t.title, style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13),
            maxLines: 1, overflow: TextOverflow.ellipsis),
        const SizedBox(height: 2),
        Text('${t.empName} · ${t.empDept}  •  ${t.issueType}',
            style: const TextStyle(color: Colors.grey, fontSize: 11)),
      ]),
    ),
  );
}
