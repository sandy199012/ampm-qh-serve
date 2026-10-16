import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';
import '../services/api_service.dart';

// Mirrors AMPMTool desktop's dedicated "🖨️ Cartridges" tab — a separate
// feature from IT Store (own tables), issued to a Department + a free-text
// person name + printer/device, not looked up via the employee directory.
class CartridgeScreen extends StatefulWidget {
  const CartridgeScreen({super.key});
  @override
  State<CartridgeScreen> createState() => _CartridgeScreenState();
}

class _CartridgeScreenState extends State<CartridgeScreen> with SingleTickerProviderStateMixin {
  static const _types = ['Toner','Ink','Drum','Ribbon','Other'];
  static const _colors = ['Black','Cyan','Magenta','Yellow','Tri-Color'];

  late TabController _tabs;
  List<Map<String, dynamic>> _cartridges = [];
  List<Map<String, dynamic>> _issues = [];
  bool _loading = true;
  String? _error;
  final _search = TextEditingController();

  @override
  void initState() { super.initState(); _tabs = TabController(length: 2, vsync: this); _load(); }

  @override
  void dispose() { _tabs.dispose(); super.dispose(); }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final c = await ApiService.getCartridges();
      final i = await ApiService.getCartridgeIssues();
      setState(() { _cartridges = c; _issues = i; _loading = false; });
    } catch (e) {
      setState(() { _loading = false; _error = e.toString(); });
    }
  }

  int _balance(Map<String, dynamic> c) => ((c['totalQty'] ?? 0) as num).toInt() - ((c['issuedQty'] ?? 0) as num).toInt();

  List<Map<String, dynamic>> get _filtered {
    if (_search.text.isEmpty) return _cartridges;
    final q = _search.text.toLowerCase();
    return _cartridges.where((c) =>
      (c['name'] ?? '').toString().toLowerCase().contains(q) ||
      (c['brand'] ?? '').toString().toLowerCase().contains(q) ||
      (c['model'] ?? '').toString().toLowerCase().contains(q)).toList();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFFF0F4F8),
      appBar: AppBar(
        title: const Text('Cartridges'),
        backgroundColor: const Color(0xFF0A192F),
        foregroundColor: Colors.white,
        actions: [IconButton(icon: const Icon(Icons.refresh), onPressed: _load)],
        bottom: TabBar(controller: _tabs, indicatorColor: const Color(0xFF06B6D4),
          tabs: const [Tab(text: 'Stock'), Tab(text: 'Issue History')]),
      ),
      body: _loading
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
          : TabBarView(controller: _tabs, children: [_buildStockTab(), _buildIssuesTab()]),
      floatingActionButton: _loading || _error != null ? null : FloatingActionButton(
        onPressed: _showAddMenu,
        backgroundColor: const Color(0xFF059669),
        child: const Icon(Icons.add, color: Colors.white),
      ),
    );
  }

  Widget _buildStockTab() {
    return Column(children: [
      Container(color: Colors.white, padding: const EdgeInsets.all(12),
        child: TextField(controller: _search, onChanged: (_) => setState(() {}),
          decoration: InputDecoration(hintText: 'Search cartridge...',
            prefixIcon: const Icon(Icons.search, color: Colors.grey),
            border: OutlineInputBorder(borderRadius: BorderRadius.circular(10)),
            contentPadding: const EdgeInsets.symmetric(vertical: 8), isDense: true))),
      Expanded(child: _filtered.isEmpty
        ? const Center(child: Text('No cartridges found', style: TextStyle(color: Colors.grey)))
        : ListView.builder(padding: const EdgeInsets.fromLTRB(12, 8, 12, 80),
            itemCount: _filtered.length,
            itemBuilder: (_, i) => _card(_filtered[i]))),
    ]);
  }

  Widget _card(Map<String, dynamic> c) {
    final bal = _balance(c);
    final low = bal <= 2;
    return GestureDetector(
      onTap: () => _showDetail(c),
      child: Container(margin: const EdgeInsets.only(bottom: 10), padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12),
          border: Border(left: BorderSide(color: low ? const Color(0xFFDC2626) : const Color(0xFF059669), width: 4)),
          boxShadow: [BoxShadow(color: Colors.black.withOpacity(.04), blurRadius: 6)]),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text((c['name'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 14))),
            Container(padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
              decoration: BoxDecoration(color: const Color(0xFF2563EB).withOpacity(.1), borderRadius: BorderRadius.circular(20)),
              child: Text((c['type'] ?? '').toString(), style: const TextStyle(color: Color(0xFF2563EB), fontSize: 10, fontWeight: FontWeight.bold))),
          ]),
          Text('${c['brand'] ?? ''}  ·  ${c['model'] ?? ''}  ·  ${c['color'] ?? ''}'.trim(),
              style: const TextStyle(color: Colors.grey, fontSize: 11)),
          const SizedBox(height: 8),
          Row(children: [
            _qtyBadge('Total', c['totalQty'], const Color(0xFF374151)),
            const SizedBox(width: 6),
            _qtyBadge('Issued', c['issuedQty'], const Color(0xFFD97706)),
            const SizedBox(width: 6),
            _qtyBadge('Balance', bal, low ? const Color(0xFFDC2626) : const Color(0xFF059669)),
          ]),
        ])),
    );
  }

  Widget _qtyBadge(String l, dynamic v, Color c) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
    decoration: BoxDecoration(color: c.withOpacity(.1), borderRadius: BorderRadius.circular(8)),
    child: Text('$l: ${v ?? 0}', style: TextStyle(color: c, fontSize: 10, fontWeight: FontWeight.bold)));

  Widget _buildIssuesTab() {
    if (_issues.isEmpty) return const Center(child: Text('No issues found', style: TextStyle(color: Colors.grey)));
    return ListView.builder(padding: const EdgeInsets.fromLTRB(12, 12, 12, 80),
      itemCount: _issues.length, itemBuilder: (_, i) => _issueCard(_issues[i]));
  }

  Widget _issueCard(Map<String, dynamic> iss) {
    return Container(margin: const EdgeInsets.only(bottom: 10), padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12),
        border: const Border(left: BorderSide(color: Color(0xFF2563EB), width: 4)),
        boxShadow: [BoxShadow(color: Colors.black.withOpacity(.04), blurRadius: 6)]),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(children: [
          Expanded(child: Text('${iss['cartridgeName'] ?? ''}  ×${iss['qty'] ?? 1}',
              style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13))),
          Text((iss['issueDate'] ?? '').toString(), style: const TextStyle(color: Colors.grey, fontSize: 10)),
        ]),
        const SizedBox(height: 4),
        Text('${iss['issuedTo'] ?? ''} · ${iss['issuedToEmp'] ?? ''}', style: const TextStyle(color: Colors.grey, fontSize: 11)),
        if ((iss['printer'] ?? '').toString().isNotEmpty)
          Text('Printer: ${iss['printer']}', style: const TextStyle(color: Colors.grey, fontSize: 11)),
        const SizedBox(height: 8),
        Row(children: [
          TextButton.icon(onPressed: () => _print('cartridge-issue', iss['id'].toString()),
            icon: const Icon(Icons.print, size: 16), label: const Text('Print Form', style: TextStyle(fontSize: 12))),
          const Spacer(),
          TextButton.icon(onPressed: () => _confirmDeleteIssue(iss),
            icon: const Icon(Icons.delete_outline, size: 16, color: Color(0xFFDC2626)),
            label: const Text('Delete', style: TextStyle(fontSize: 12, color: Color(0xFFDC2626)))),
        ]),
      ]));
  }

  Future<void> _print(String kind, String id) async {
    try {
      final url = await ApiService.printUrl(kind, id);
      final uri = Uri.parse(url);
      final ok = await launchUrl(uri, mode: LaunchMode.externalApplication);
      if (!ok && mounted) ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Could not open browser')));
    } catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e')));
    }
  }

  Future<void> _confirmDeleteIssue(Map<String, dynamic> iss) async {
    final ok = await showDialog<bool>(context: context, builder: (_) => AlertDialog(
      title: const Text('Delete this issue?'),
      content: Text('${iss['cartridgeName']} → ${iss['issuedToEmp']} — the balance will be added back.'),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
        TextButton(onPressed: () => Navigator.pop(context, true),
          child: const Text('Delete', style: TextStyle(color: Colors.red, fontWeight: FontWeight.bold))),
      ]));
    if (ok != true) return;
    try {
      await ApiService.deleteCartridgeIssue(iss['id'].toString());
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Issue deleted, balance restored'), backgroundColor: Color(0xFF059669)));
      _load();
    } catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
    }
  }

  void _showDetail(Map<String, dynamic> c) {
    final bal = _balance(c);
    showModalBottomSheet(context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => Padding(
        padding: EdgeInsets.fromLTRB(20, 20, 20, MediaQuery.of(ctx).viewInsets.bottom + 20),
        child: SingleChildScrollView(child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            const Spacer(),
            IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
          ]),
          Text((c['name'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18)),
          Text('${c['type'] ?? ''} · ${c['color'] ?? ''}', style: const TextStyle(color: Colors.grey, fontSize: 13)),
          const SizedBox(height: 12),
          Row(children: [
            _qtyBadge('Total', c['totalQty'], const Color(0xFF374151)),
            const SizedBox(width: 8),
            _qtyBadge('Issued', c['issuedQty'], const Color(0xFFD97706)),
            const SizedBox(width: 8),
            _qtyBadge('Balance', bal, const Color(0xFF059669)),
          ]),
          const SizedBox(height: 12),
          if ((c['brand'] ?? '').toString().isNotEmpty) Text('Brand: ${c['brand']}', style: const TextStyle(fontSize: 12, color: Colors.grey)),
          if ((c['model'] ?? '').toString().isNotEmpty) Text('Compatible Printer: ${c['model']}', style: const TextStyle(fontSize: 12, color: Colors.grey)),
          if ((c['vendor'] ?? '').toString().isNotEmpty) Text('Vendor: ${c['vendor']}', style: const TextStyle(fontSize: 12, color: Colors.grey)),
          if ((c['unitCost'] ?? 0) != 0) Text('Unit Cost: ₹${c['unitCost']}', style: const TextStyle(fontSize: 12, color: Colors.grey)),
          if ((c['location'] ?? '').toString().isNotEmpty) Text('Location: ${c['location']}', style: const TextStyle(fontSize: 12, color: Colors.grey)),
          if ((c['notes'] ?? '').toString().isNotEmpty) Text('Notes: ${c['notes']}', style: const TextStyle(fontSize: 12, color: Colors.grey)),
          const SizedBox(height: 20),
          Row(children: [
            Expanded(child: OutlinedButton.icon(
              onPressed: () { Navigator.pop(ctx); _showEditCartridge(c); },
              icon: const Icon(Icons.edit), label: const Text('Edit'))),
            const SizedBox(width: 10),
            Expanded(child: ElevatedButton.icon(
              onPressed: bal <= 0 ? null : () { Navigator.pop(ctx); _showIssue(c); },
              icon: const Icon(Icons.outbox), label: const Text('Issue'),
              style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF2563EB), foregroundColor: Colors.white))),
          ]),
        ])),
      ));
  }

  void _showAddMenu() {
    showModalBottomSheet(context: context, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => SafeArea(child: Column(mainAxisSize: MainAxisSize.min, children: [
        Row(children: [
          const Spacer(),
          IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx)),
        ]),
        ListTile(leading: const Icon(Icons.add_circle, color: Color(0xFF059669)), title: const Text('Add New Cartridge'),
          onTap: () { Navigator.pop(ctx); _showEditCartridge(null); }),
        ListTile(leading: const Icon(Icons.outbox, color: Color(0xFFD97706)), title: const Text('Issue Cartridge'),
          onTap: () { Navigator.pop(ctx); _pickCartridgeThen(_showIssue); }),
      ])));
  }

  void _pickCartridgeThen(void Function(Map<String, dynamic>) action) {
    if (_cartridges.isEmpty) { ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Add a cartridge first!'))); return; }
    showModalBottomSheet(context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => SizedBox(height: MediaQuery.of(ctx).size.height * .6,
        child: Column(children: [
          Row(children: [
            const Padding(padding: EdgeInsets.only(left: 16, top: 8), child: Text('Select Cartridge', style: TextStyle(fontWeight: FontWeight.bold))),
            const Spacer(),
            IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx)),
          ]),
          Expanded(child: ListView.builder(padding: const EdgeInsets.all(12), itemCount: _cartridges.length,
            itemBuilder: (_, i) { final c = _cartridges[i]; return ListTile(
              title: Text((c['name'] ?? '').toString()),
              subtitle: Text('${c['type'] ?? ''}  |  Balance: ${_balance(c)}'),
              onTap: () { Navigator.pop(ctx); action(c); },
            ); })),
        ])));
  }

  void _showEditCartridge(Map<String, dynamic>? existing) {
    final isEdit = existing != null;
    final name = TextEditingController(text: (existing?['name'] ?? '').toString());
    final brand = TextEditingController(text: (existing?['brand'] ?? '').toString());
    final model = TextEditingController(text: (existing?['model'] ?? '').toString());
    final vendor = TextEditingController(text: (existing?['vendor'] ?? '').toString());
    final location = TextEditingController(text: (existing?['location'] ?? 'IT Store').toString());
    final notes = TextEditingController(text: (existing?['notes'] ?? '').toString());
    final totalQty = TextEditingController(text: (existing?['totalQty'] ?? 0).toString());
    final unitCost = TextEditingController(text: ((existing?['unitCost'] ?? 0) == 0) ? '' : existing!['unitCost'].toString());
    String type = _types.contains(existing?['type']) ? existing!['type'] : _types[0];
    String color = _colors.contains(existing?['color']) ? existing!['color'] : _colors[0];
    bool saving = false;

    showModalBottomSheet(context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => StatefulBuilder(builder: (ctx, setS) => Padding(
        padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(ctx).viewInsets.bottom + 16),
        child: SingleChildScrollView(child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text(isEdit ? 'Edit Cartridge' : 'New Cartridge', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18))),
            IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
          ]),
          const SizedBox(height: 12),
          TextField(controller: name, decoration: const InputDecoration(labelText: 'Cartridge Name *', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: brand, decoration: const InputDecoration(labelText: 'Brand', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: model, decoration: const InputDecoration(labelText: 'Compatible Printer', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          Row(children: [
            Expanded(child: DropdownButtonFormField<String>(value: type, isExpanded: true,
              items: _types.map((t) => DropdownMenuItem(value: t, child: Text(t))).toList(),
              onChanged: (v) => setS(() => type = v!),
              decoration: const InputDecoration(labelText: 'Type', border: OutlineInputBorder()))),
            const SizedBox(width: 8),
            Expanded(child: DropdownButtonFormField<String>(value: color, isExpanded: true,
              items: _colors.map((t) => DropdownMenuItem(value: t, child: Text(t))).toList(),
              onChanged: (v) => setS(() => color = v!),
              decoration: const InputDecoration(labelText: 'Color', border: OutlineInputBorder()))),
          ]),
          const SizedBox(height: 10),
          TextField(controller: totalQty, keyboardType: TextInputType.number,
            decoration: InputDecoration(labelText: isEdit ? 'Total Qty (in stock)' : 'Opening Qty', border: const OutlineInputBorder())),
          if (isEdit)
            const Padding(padding: EdgeInsets.only(top: 4),
              child: Text('Edit Total Qty directly — set it to the actual current stock', style: TextStyle(fontSize: 10, color: Colors.grey))),
          const SizedBox(height: 10),
          TextField(controller: unitCost, keyboardType: TextInputType.number,
            decoration: const InputDecoration(labelText: 'Unit Cost (₹)', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: vendor, decoration: const InputDecoration(labelText: 'Vendor', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: location, decoration: const InputDecoration(labelText: 'Location', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: notes, maxLines: 2, decoration: const InputDecoration(labelText: 'Notes', border: OutlineInputBorder())),
          const SizedBox(height: 16),
          SizedBox(width: double.infinity, height: 48, child: ElevatedButton(
            onPressed: saving ? null : () async {
              if (name.text.trim().isEmpty) { ScaffoldMessenger.of(ctx).showSnackBar(const SnackBar(content: Text('Name required!'))); return; }
              setS(() => saving = true);
              try {
                final data = {
                  if (isEdit) 'id': existing!['id'],
                  if (isEdit) 'issuedQty': existing!['issuedQty'] ?? 0,
                  'name': name.text.trim(), 'brand': brand.text.trim(), 'model': model.text.trim(),
                  'type': type, 'color': color,
                  'totalQty': int.tryParse(totalQty.text.trim()) ?? 0,
                  'unitCost': double.tryParse(unitCost.text.trim()) ?? 0,
                  'vendor': vendor.text.trim(), 'location': location.text.trim(), 'notes': notes.text.trim(),
                };
                await ApiService.saveCartridge(data);
                if (ctx.mounted) Navigator.pop(ctx);
                if (mounted) ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(content: Text(isEdit ? 'Cartridge updated!' : 'Cartridge added!'), backgroundColor: const Color(0xFF059669)));
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

  Future<void> _showIssue(Map<String, dynamic> c) async {
    List<String> departments = [];
    try { departments = await ApiService.getDepartments(); } catch (_) {}
    if (!mounted) return;
    String? dept = departments.isNotEmpty ? departments[0] : null;
    final person = TextEditingController();
    final printer = TextEditingController(text: (c['model'] ?? '').toString());
    final qty = TextEditingController(text: '1');
    final remarks = TextEditingController();
    bool saving = false;
    final bal = _balance(c);

    showModalBottomSheet(context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => StatefulBuilder(builder: (ctx, setS) => Padding(
        padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(ctx).viewInsets.bottom + 16),
        child: SingleChildScrollView(child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text('Issue — ${c['name']}  (Balance: $bal)', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16))),
            IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
          ]),
          const SizedBox(height: 12),
          DropdownButtonFormField<String>(value: dept, isExpanded: true,
            items: departments.map((d) => DropdownMenuItem(value: d, child: Text(d))).toList(),
            onChanged: (v) => setS(() => dept = v),
            decoration: const InputDecoration(labelText: 'Department *', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: person, decoration: const InputDecoration(labelText: 'Issued To (Person)', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: printer, decoration: const InputDecoration(labelText: 'Printer / Device', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: qty, keyboardType: TextInputType.number,
            decoration: const InputDecoration(labelText: 'Qty to Issue *', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: remarks, decoration: const InputDecoration(labelText: 'Remarks', border: OutlineInputBorder())),
          const SizedBox(height: 16),
          SizedBox(width: double.infinity, height: 48, child: ElevatedButton(
            onPressed: saving ? null : () async {
              final q = int.tryParse(qty.text.trim()) ?? 0;
              if (q <= 0) { ScaffoldMessenger.of(ctx).showSnackBar(const SnackBar(content: Text('Enter a quantity!'))); return; }
              if (dept == null) { ScaffoldMessenger.of(ctx).showSnackBar(const SnackBar(content: Text('Select a department!'))); return; }
              setS(() => saving = true);
              try {
                await ApiService.issueCartridge({
                  'cartridgeId': c['id'], 'qty': q, 'dept': dept, 'personName': person.text.trim(),
                  'printer': printer.text.trim(), 'issuedBy': 'IT Admin', 'remarks': remarks.text.trim(),
                });
                if (ctx.mounted) Navigator.pop(ctx);
                if (mounted) ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(content: Text('Cartridge issued!'), backgroundColor: Color(0xFF059669)));
                _load();
              } catch (e) {
                setS(() => saving = false);
                if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
              }
            },
            style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFFD97706), foregroundColor: Colors.white),
            child: saving
              ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
              : const Text('Issue', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 15)),
          )),
          const SizedBox(height: 8),
        ])),
      )));
  }
}
