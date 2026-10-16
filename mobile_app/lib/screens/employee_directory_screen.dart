import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';
import '../services/api_service.dart';

class EmployeeDirectoryScreen extends StatefulWidget {
  const EmployeeDirectoryScreen({super.key});
  @override
  State<EmployeeDirectoryScreen> createState() => _EmployeeDirectoryScreenState();
}

class _EmployeeDirectoryScreenState extends State<EmployeeDirectoryScreen> {
  List<Map<String, dynamic>> _employees = [];
  List<Map<String, dynamic>> _filtered = [];
  bool _loading = true;
  String? _error;
  final _search = TextEditingController();
  String _dept = 'All';

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final e = await ApiService.getEmployeesFull();
      setState(() { _employees = e; _loading = false; });
      _applyFilter();
    } catch (e) {
      setState(() { _loading = false; _error = e.toString(); });
    }
  }

  List<String> get _depts {
    final s = _employees.map((e) => (e['dept'] ?? '').toString()).where((d) => d.isNotEmpty).toSet().toList();
    s.sort();
    return ['All', ...s];
  }

  void _applyFilter() {
    var list = _employees.toList();
    if (_dept != 'All') list = list.where((e) => (e['dept'] ?? '') == _dept).toList();
    if (_search.text.isNotEmpty) {
      final q = _search.text.toLowerCase();
      list = list.where((e) =>
        (e['name'] ?? '').toString().toLowerCase().contains(q) ||
        (e['empId'] ?? '').toString().toLowerCase().contains(q) ||
        (e['dept'] ?? '').toString().toLowerCase().contains(q) ||
        (e['designation'] ?? '').toString().toLowerCase().contains(q)).toList();
    }
    list.sort((a, b) => (a['name'] ?? '').toString().compareTo((b['name'] ?? '').toString()));
    setState(() => _filtered = list);
  }

  Future<void> _call(String mobile) async {
    if (mobile.trim().isEmpty) return;
    final uri = Uri(scheme: 'tel', path: mobile.trim());
    try {
      await launchUrl(uri);
    } catch (_) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Could not place the call')));
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFFF5F2FF),
      appBar: AppBar(
        title: const Text('Employee Directory'),
        backgroundColor: const Color(0xFF130A33),
        foregroundColor: Colors.white,
        actions: [IconButton(icon: const Icon(Icons.refresh), onPressed: _load)],
      ),
      body: Column(children: [
        Container(
          color: Colors.white,
          padding: const EdgeInsets.all(12),
          child: Column(children: [
            TextField(
              controller: _search,
              onChanged: (_) => _applyFilter(),
              decoration: InputDecoration(
                hintText: 'Search by name, code, department...',
                prefixIcon: const Icon(Icons.search, color: Colors.grey),
                border: OutlineInputBorder(borderRadius: BorderRadius.circular(10)),
                contentPadding: const EdgeInsets.symmetric(vertical: 8),
                isDense: true,
              ),
            ),
            if (!_loading && _error == null) ...[
              const SizedBox(height: 8),
              SizedBox(
                height: 34,
                child: ListView(
                  scrollDirection: Axis.horizontal,
                  children: _depts.map((d) => Padding(
                    padding: const EdgeInsets.only(right: 8),
                    child: FilterChip(
                      label: Text(d, style: TextStyle(
                        color: _dept == d ? Colors.white : const Color(0xFF3B3556),
                        fontWeight: FontWeight.w600, fontSize: 12)),
                      selected: _dept == d,
                      onSelected: (_) { setState(() => _dept = d); _applyFilter(); },
                      selectedColor: const Color(0xFF059669),
                      backgroundColor: const Color(0xFFEFEAFB),
                      side: BorderSide.none,
                    ),
                  )).toList(),
                ),
              ),
            ],
          ]),
        ),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
          child: Row(children: [
            Text(_loading ? '...' : '${_filtered.length} employees', style: const TextStyle(color: Colors.grey, fontSize: 12)),
          ]),
        ),
        Expanded(
          child: _loading
            ? const Center(child: CircularProgressIndicator())
            : _error != null
              ? Center(child: Padding(padding: const EdgeInsets.all(24), child: Column(
                  mainAxisAlignment: MainAxisAlignment.center, children: [
                    const Icon(Icons.wifi_off, size: 48, color: Color(0xFFDC2626)),
                    const SizedBox(height: 12),
                    Text(_error!, style: const TextStyle(color: Colors.grey, fontSize: 12), textAlign: TextAlign.center),
                    const SizedBox(height: 12),
                    ElevatedButton(onPressed: _load, child: const Text('Retry')),
                  ])))
              : _filtered.isEmpty
                ? const Center(child: Text('No employees found', style: TextStyle(color: Colors.grey)))
                : ListView.builder(
                    padding: const EdgeInsets.symmetric(horizontal: 12),
                    itemCount: _filtered.length,
                    itemBuilder: (_, i) => _card(_filtered[i]),
                  ),
        ),
      ]),
    );
  }

  Widget _card(Map<String, dynamic> e) {
    final name = (e['name'] ?? '').toString();
    final empId = (e['empId'] ?? '').toString();
    final dept = (e['dept'] ?? '').toString();
    final desig = (e['designation'] ?? '').toString();
    final mobile = (e['mobile'] ?? '').toString();
    final email = (e['email'] ?? '').toString();
    final initial = name.isNotEmpty ? name[0].toUpperCase() : '?';

    return Container(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(12),
        boxShadow: [BoxShadow(color: Colors.black.withOpacity(.04), blurRadius: 6)],
      ),
      child: Row(children: [
        CircleAvatar(radius: 22, backgroundColor: const Color(0xFF6D4AFF).withOpacity(.12),
            child: Text(initial, style: const TextStyle(color: Color(0xFF6D4AFF), fontWeight: FontWeight.bold, fontSize: 16))),
        const SizedBox(width: 12),
        Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text(name, style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 14)),
          const SizedBox(height: 2),
          Text('$empId  •  $dept${desig.isNotEmpty ? " • $desig" : ""}',
              style: const TextStyle(color: Colors.grey, fontSize: 11)),
          if (email.isNotEmpty) ...[
            const SizedBox(height: 2),
            Text(email, style: const TextStyle(color: Colors.grey, fontSize: 11)),
          ],
        ])),
        if (mobile.isNotEmpty)
          IconButton(
            icon: const Icon(Icons.call, color: Color(0xFF059669)),
            onPressed: () => _call(mobile),
            tooltip: 'Call',
          ),
      ]),
    );
  }
}
