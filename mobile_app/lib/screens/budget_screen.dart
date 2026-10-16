import 'package:flutter/material.dart';
import '../services/api_service.dart';

// Mirrors AMPMTool desktop's "Budget" tab (kv key "budget") — each item has
// a Monthly dict keyed "MMMyy" (e.g. "Jul26") with projected/actual. Actual
// gets posted automatically when a Purchase Bill links to this budget item
// (see PurchaseBillsScreen) — this screen lets you set/adjust Projected.
class BudgetScreen extends StatefulWidget {
  const BudgetScreen({super.key});
  @override
  State<BudgetScreen> createState() => _BudgetScreenState();
}

class _BudgetScreenState extends State<BudgetScreen> {
  static const _sections = ['Hardware','Software','AMC','Networking','Misc'];
  static const _types = ['Capex','Opex'];

  List<Map<String, dynamic>> _budget = [];
  bool _loading = true;
  String? _error;

  @override
  void initState() { super.initState(); _load(); }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final b = await ApiService.getBudget();
      setState(() { _budget = b; _loading = false; });
    } catch (e) {
      setState(() { _loading = false; _error = e.toString(); });
    }
  }

  String get _currentMonthKey {
    final now = DateTime.now();
    return '${_monthAbbr(now.month)}${now.year.toString().substring(2)}';
  }

  String _monthAbbr(int m) =>
    const ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'][m - 1];

  double _totalProjected(Map<String, dynamic> b) {
    final monthly = (b['monthly'] ?? {}) as Map;
    return monthly.values.fold(0.0, (s, m) => s + (((m as Map)['projected'] ?? 0) as num).toDouble());
  }

  double _totalActual(Map<String, dynamic> b) {
    final monthly = (b['monthly'] ?? {}) as Map;
    return monthly.values.fold(0.0, (s, m) => s + (((m as Map)['actual'] ?? 0) as num).toDouble());
  }

  @override
  Widget build(BuildContext context) {
    final totalProj = _budget.fold(0.0, (s, b) => s + _totalProjected(b));
    final totalAct = _budget.fold(0.0, (s, b) => s + _totalActual(b));
    return Scaffold(
      backgroundColor: const Color(0xFFF5F2FF),
      appBar: AppBar(
        title: const Text('Budget'),
        backgroundColor: const Color(0xFF130A33),
        foregroundColor: Colors.white,
        actions: [IconButton(icon: const Icon(Icons.refresh), onPressed: _load)],
      ),
      body: Column(children: [
        if (!_loading && _error == null)
          Container(width: double.infinity, color: const Color(0xFFF0EBFF), padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
            child: Row(children: [
              Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                const Text('Projected', style: TextStyle(color: Colors.grey, fontSize: 10)),
                Text('₹${totalProj.toStringAsFixed(0)}', style: const TextStyle(color: Color(0xFF6D4AFF), fontWeight: FontWeight.bold, fontSize: 14)),
              ])),
              Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                const Text('Actual', style: TextStyle(color: Colors.grey, fontSize: 10)),
                Text('₹${totalAct.toStringAsFixed(0)}', style: const TextStyle(color: Color(0xFF059669), fontWeight: FontWeight.bold, fontSize: 14)),
              ])),
              Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                const Text('Variance', style: TextStyle(color: Colors.grey, fontSize: 10)),
                Text('₹${(totalProj - totalAct).toStringAsFixed(0)}',
                  style: TextStyle(color: (totalProj-totalAct)<0 ? const Color(0xFFDC2626) : const Color(0xFF3B3556), fontWeight: FontWeight.bold, fontSize: 14)),
              ])),
            ])),
        Expanded(child: _loading
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
            : _budget.isEmpty
              ? const Center(child: Text('No budget items', style: TextStyle(color: Colors.grey)))
              : ListView.builder(padding: const EdgeInsets.fromLTRB(12, 8, 12, 80),
                  itemCount: _budget.length, itemBuilder: (_, i) => _card(_budget[i]))),
      ]),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _showEdit(null),
        backgroundColor: const Color(0xFF059669),
        icon: const Icon(Icons.add, color: Colors.white),
        label: const Text('New Budget Item', style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold)),
      ),
    );
  }

  Widget _card(Map<String, dynamic> b) {
    final proj = _totalProjected(b);
    final act = _totalActual(b);
    final over = act > proj && proj > 0;
    return GestureDetector(
      onTap: () => _showDetail(b),
      child: Container(margin: const EdgeInsets.only(bottom: 10), padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12),
          border: Border(left: BorderSide(color: over ? const Color(0xFFDC2626) : const Color(0xFF6D4AFF), width: 4)),
          boxShadow: [BoxShadow(color: Colors.black.withOpacity(.04), blurRadius: 6)]),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text((b['description'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 14))),
            Container(padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
              decoration: BoxDecoration(color: const Color(0xFF6D4AFF).withOpacity(.1), borderRadius: BorderRadius.circular(20)),
              child: Text((b['type'] ?? '').toString(), style: const TextStyle(color: Color(0xFF6D4AFF), fontSize: 10, fontWeight: FontWeight.bold))),
          ]),
          Text((b['section'] ?? '').toString(), style: const TextStyle(color: Colors.grey, fontSize: 11)),
          const SizedBox(height: 6),
          Row(children: [
            Text('Proj: ₹${proj.toStringAsFixed(0)}', style: const TextStyle(fontSize: 12, color: Color(0xFF6D4AFF), fontWeight: FontWeight.w600)),
            const SizedBox(width: 10),
            Text('Actual: ₹${act.toStringAsFixed(0)}', style: TextStyle(fontSize: 12, color: over ? const Color(0xFFDC2626) : const Color(0xFF059669), fontWeight: FontWeight.w600)),
          ]),
        ])),
    );
  }

  void _showDetail(Map<String, dynamic> b) {
    final monthly = Map<String, dynamic>.from(b['monthly'] ?? {});
    final months = monthly.keys.toList()..sort();
    showModalBottomSheet(
      context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => Padding(
        padding: EdgeInsets.fromLTRB(20, 20, 20, MediaQuery.of(ctx).viewInsets.bottom + 20),
        child: SingleChildScrollView(child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            const Spacer(),
            IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
          ]),
          Text((b['description'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18)),
          Text('${b['section'] ?? ''} · ${b['type'] ?? ''}', style: const TextStyle(color: Colors.grey, fontSize: 13)),
          const SizedBox(height: 16),
          if (months.isEmpty)
            const Text('No monthly entries', style: TextStyle(color: Colors.grey, fontSize: 12))
          else
            ...months.map((m) {
              final md = Map<String, dynamic>.from(monthly[m] ?? {});
              return Padding(padding: const EdgeInsets.only(bottom: 6), child: Row(children: [
                SizedBox(width: 60, child: Text(m, style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 12))),
                Expanded(child: Text('Proj: ₹${md['projected'] ?? 0}', style: const TextStyle(fontSize: 12, color: Color(0xFF6D4AFF)))),
                Expanded(child: Text('Actual: ₹${md['actual'] ?? 0}', style: const TextStyle(fontSize: 12, color: Color(0xFF059669)))),
              ]));
            }),
          const SizedBox(height: 20),
          Row(children: [
            Expanded(child: OutlinedButton.icon(
              onPressed: () { Navigator.pop(ctx); _showEdit(b); },
              icon: const Icon(Icons.edit), label: const Text('Edit / Set Month'))),
            const SizedBox(width: 10),
            Expanded(child: OutlinedButton.icon(
              onPressed: () { Navigator.pop(ctx); _confirmDelete(b); },
              icon: const Icon(Icons.delete_outline, color: Color(0xFFDC2626)),
              label: const Text('Delete', style: TextStyle(color: Color(0xFFDC2626))),
              style: OutlinedButton.styleFrom(side: const BorderSide(color: Color(0xFFDC2626))))),
          ]),
        ])),
      ),
    );
  }

  Future<void> _confirmDelete(Map<String, dynamic> b) async {
    final ok = await showDialog<bool>(context: context, builder: (_) => AlertDialog(
      title: const Text('Delete this budget item?'),
      content: Text('${b['description']} will be permanently deleted.'),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
        TextButton(onPressed: () => Navigator.pop(context, true),
          child: const Text('Delete', style: TextStyle(color: Colors.red, fontWeight: FontWeight.bold))),
      ]));
    if (ok != true) return;
    try {
      await ApiService.deleteBudgetItem(b['id'].toString());
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Budget item deleted'), backgroundColor: Color(0xFF059669)));
      _load();
    } catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
    }
  }

  void _showEdit(Map<String, dynamic>? existing) {
    final isEdit = existing != null;
    final desc = TextEditingController(text: (existing?['description'] ?? '').toString());
    final monthKey = TextEditingController(text: _currentMonthKey);
    final monthly = Map<String, dynamic>.from(existing?['monthly'] ?? {});
    final existingProjected = monthly[_currentMonthKey]?['projected'];
    final projected = TextEditingController(text: (existingProjected ?? 0).toString());
    String section = _sections.contains(existing?['section']) ? existing!['section'] : _sections[0];
    String type = _types.contains(existing?['type']) ? existing!['type'] : _types[0];
    bool saving = false;

    showModalBottomSheet(context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => StatefulBuilder(builder: (ctx, setS) => Padding(
        padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(ctx).viewInsets.bottom + 16),
        child: SingleChildScrollView(child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text(isEdit ? 'Edit Budget Item' : 'New Budget Item', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18))),
            IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
          ]),
          const SizedBox(height: 12),
          TextField(controller: desc, decoration: const InputDecoration(labelText: 'Description *', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          Row(children: [
            Expanded(child: DropdownButtonFormField<String>(value: section, isExpanded: true,
              items: _sections.map((t) => DropdownMenuItem(value: t, child: Text(t))).toList(),
              onChanged: (v) => setS(() => section = v!),
              decoration: const InputDecoration(labelText: 'Section', border: OutlineInputBorder()))),
            const SizedBox(width: 8),
            Expanded(child: DropdownButtonFormField<String>(value: type, isExpanded: true,
              items: _types.map((t) => DropdownMenuItem(value: t, child: Text(t))).toList(),
              onChanged: (v) => setS(() => type = v!),
              decoration: const InputDecoration(labelText: 'Type', border: OutlineInputBorder()))),
          ]),
          const SizedBox(height: 14),
          const Divider(),
          const SizedBox(height: 6),
          const Text('Set Monthly Projected', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 13)),
          const SizedBox(height: 8),
          Row(children: [
            Expanded(child: TextField(controller: monthKey, decoration: const InputDecoration(labelText: 'Month (e.g. Jul26)', border: OutlineInputBorder()))),
            const SizedBox(width: 8),
            Expanded(child: TextField(controller: projected, keyboardType: TextInputType.number,
              decoration: const InputDecoration(labelText: 'Projected (₹)', border: OutlineInputBorder()))),
          ]),
          const SizedBox(height: 16),
          SizedBox(width: double.infinity, height: 48, child: ElevatedButton(
            onPressed: saving ? null : () async {
              if (desc.text.trim().isEmpty) { ScaffoldMessenger.of(ctx).showSnackBar(const SnackBar(content: Text('Description required!'))); return; }
              setS(() => saving = true);
              try {
                final mk = monthKey.text.trim();
                if (mk.isNotEmpty) {
                  final existingMonth = Map<String, dynamic>.from(monthly[mk] ?? {'projected': 0, 'actual': 0});
                  existingMonth['projected'] = double.tryParse(projected.text.trim()) ?? 0;
                  monthly[mk] = existingMonth;
                }
                final data = {
                  if (isEdit) 'id': existing!['id'],
                  'description': desc.text.trim(), 'section': section, 'type': type, 'monthly': monthly,
                };
                await ApiService.saveBudgetItem(data);
                if (ctx.mounted) Navigator.pop(ctx);
                if (mounted) ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(content: Text(isEdit ? 'Budget item updated!' : 'Budget item added!'), backgroundColor: const Color(0xFF059669)));
                _load();
              } catch (e) {
                setS(() => saving = false);
                if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
              }
            },
            style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF059669), foregroundColor: Colors.white),
            child: saving
              ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
              : Text(isEdit ? 'Update' : 'Add', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 15)),
          )),
          const SizedBox(height: 8),
        ])),
      )));
  }
}
