import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';
import '../services/api_service.dart';

// Shared screen for both "IT Store" and "Peripheral Stock" — the two AMPMTool
// desktop tabs are near-identical (item + stock-in + issue/return) but back
// separate tables, so this widget just switches which ApiService calls it
// makes based on [peripheral].
class StockModuleScreen extends StatefulWidget {
  final bool peripheral;
  const StockModuleScreen({super.key, required this.peripheral});
  @override
  State<StockModuleScreen> createState() => _StockModuleScreenState();
}

class _StockModuleScreenState extends State<StockModuleScreen> with SingleTickerProviderStateMixin {
  static const _itStoreTypes = ['Keyboard','Mouse','Headset','Webcam','Pendrive','Cable',
    'Adapter','Hub','Monitor','UPS','Cartridge','Speaker','Other'];
  static const _peripheralCats = ['Keyboard','Mouse','Headset','Webcam','Pendrive','Cable',
    'Adapter','Hub/Switch','Monitor','UPS','Printer Cartridge','Other'];

  late TabController _tabs;
  List<Map<String, dynamic>> _items = [];
  List<Map<String, dynamic>> _issues = [];
  bool _loading = true;
  String? _error;
  final _search = TextEditingController();
  String _typeFilter = 'All';

  bool get _p => widget.peripheral;
  String get _typeKey => _p ? 'category' : 'itemType';
  String get _typeLabel => _p ? 'Category' : 'Item Type';
  String get _reasonKey => _p ? 'reason' : 'purpose';
  String get _reasonLabel => _p ? 'Reason' : 'Purpose';
  List<String> get _typeOptions => _p ? _peripheralCats : _itStoreTypes;

  @override
  void initState() { super.initState(); _tabs = TabController(length: 2, vsync: this); _load(); }

  @override
  void dispose() { _tabs.dispose(); super.dispose(); }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final items = _p ? await ApiService.getPeripheralItems() : await ApiService.getStoreItems();
      final issues = _p ? await ApiService.getPeripheralIssues() : await ApiService.getStoreIssues();
      setState(() { _items = items; _issues = issues; _loading = false; });
    } catch (e) {
      setState(() { _loading = false; _error = e.toString(); });
    }
  }

  int _avail(Map<String, dynamic> it) =>
      ((it['totalQty'] ?? 0) as num).toInt() - ((it['issuedQty'] ?? 0) as num).toInt() - ((it['damagedQty'] ?? 0) as num).toInt();

  List<Map<String, dynamic>> get _filteredItems {
    var list = _items.toList();
    if (_typeFilter != 'All') list = list.where((i) => (i[_typeKey] ?? '') == _typeFilter).toList();
    if (_search.text.isNotEmpty) {
      final q = _search.text.toLowerCase();
      list = list.where((i) =>
        (i['name'] ?? '').toString().toLowerCase().contains(q) ||
        (i['brand'] ?? '').toString().toLowerCase().contains(q) ||
        (i[_typeKey] ?? '').toString().toLowerCase().contains(q)).toList();
    }
    return list;
  }

  @override
  Widget build(BuildContext context) {
    final title = _p ? 'Peripheral Stock' : 'IT Store';
    return Scaffold(
      backgroundColor: const Color(0xFFF0F4F8),
      appBar: AppBar(
        title: Text(title),
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
      Container(color: Colors.white, padding: const EdgeInsets.all(12), child: Column(children: [
        TextField(controller: _search, onChanged: (_) => setState(() {}),
          decoration: InputDecoration(hintText: 'Search item...',
            prefixIcon: const Icon(Icons.search, color: Colors.grey),
            border: OutlineInputBorder(borderRadius: BorderRadius.circular(10)),
            contentPadding: const EdgeInsets.symmetric(vertical: 8), isDense: true)),
        const SizedBox(height: 8),
        SizedBox(height: 34, child: ListView(scrollDirection: Axis.horizontal,
          children: ['All', ..._typeOptions].map((t) => Padding(
            padding: const EdgeInsets.only(right: 8),
            child: FilterChip(
              label: Text(t, style: TextStyle(color: _typeFilter == t ? Colors.white : const Color(0xFF374151), fontWeight: FontWeight.w600, fontSize: 12)),
              selected: _typeFilter == t,
              onSelected: (_) => setState(() => _typeFilter = t),
              selectedColor: const Color(0xFF059669),
              backgroundColor: const Color(0xFFF3F4F6), side: BorderSide.none,
            ))).toList())),
      ])),
      Expanded(child: _filteredItems.isEmpty
        ? const Center(child: Text('No items found', style: TextStyle(color: Colors.grey)))
        : ListView.builder(padding: const EdgeInsets.fromLTRB(12, 8, 12, 80),
            itemCount: _filteredItems.length,
            itemBuilder: (_, i) => _itemCard(_filteredItems[i]))),
    ]);
  }

  Widget _itemCard(Map<String, dynamic> it) {
    final avail = _avail(it);
    final low = avail <= ((it['minStock'] ?? 2) as num).toInt();
    return GestureDetector(
      onTap: () => _showItemDetail(it),
      child: Container(margin: const EdgeInsets.only(bottom: 10), padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12),
          border: Border(left: BorderSide(color: low ? const Color(0xFFDC2626) : const Color(0xFF059669), width: 4)),
          boxShadow: [BoxShadow(color: Colors.black.withOpacity(.04), blurRadius: 6)]),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text((it['name'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 14))),
            Container(padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
              decoration: BoxDecoration(color: const Color(0xFF2563EB).withOpacity(.1), borderRadius: BorderRadius.circular(20)),
              child: Text((it[_typeKey] ?? '').toString(), style: const TextStyle(color: Color(0xFF2563EB), fontSize: 10, fontWeight: FontWeight.bold))),
          ]),
          if ((it['brand'] ?? '').toString().isNotEmpty)
            Text((it['brand'] ?? '').toString(), style: const TextStyle(color: Colors.grey, fontSize: 11)),
          const SizedBox(height: 8),
          Row(children: [
            _qtyBadge('Total', it['totalQty'], const Color(0xFF374151)),
            const SizedBox(width: 6),
            _qtyBadge('Issued', it['issuedQty'], const Color(0xFFD97706)),
            const SizedBox(width: 6),
            _qtyBadge('Avail', avail, low ? const Color(0xFFDC2626) : const Color(0xFF059669)),
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
      itemCount: _issues.length,
      itemBuilder: (_, i) => _issueCard(_issues[i]));
  }

  Color _issueStatusColor(String s) {
    switch (s) {
      case 'Issued':   return const Color(0xFFD97706);
      case 'Returned':  return const Color(0xFF059669);
      case 'Damaged':   return const Color(0xFFDC2626);
      default:          return const Color(0xFF6B7280);
    }
  }

  Widget _issueCard(Map<String, dynamic> iss) {
    final status = (iss['status'] ?? '').toString();
    final c = _issueStatusColor(status);
    return Container(margin: const EdgeInsets.only(bottom: 10), padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12),
          border: Border(left: BorderSide(color: c, width: 4)),
          boxShadow: [BoxShadow(color: Colors.black.withOpacity(.04), blurRadius: 6)]),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          GestureDetector(
            onTap: status == 'Issued' ? () => _showReturnDialog(iss) : null,
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Row(children: [
                Text((iss['issueNo'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, color: Color(0xFF2563EB), fontSize: 12)),
                const SizedBox(width: 8),
                Container(padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                  decoration: BoxDecoration(color: c.withOpacity(.1), borderRadius: BorderRadius.circular(20)),
                  child: Text(status, style: TextStyle(color: c, fontSize: 10, fontWeight: FontWeight.bold))),
                const Spacer(),
                Text((iss['issueDate'] ?? '').toString(), style: const TextStyle(color: Colors.grey, fontSize: 10)),
              ]),
              const SizedBox(height: 6),
              Text('${iss['itemName'] ?? ''}  ×${iss['qty'] ?? 1}', style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13)),
              const SizedBox(height: 2),
              Text('${iss['empName'] ?? ''} · ${iss['empDept'] ?? ''}', style: const TextStyle(color: Colors.grey, fontSize: 11)),
              if (status == 'Issued')
                const Padding(padding: EdgeInsets.only(top: 6),
                  child: Text('Tap to process the return', style: TextStyle(color: Color(0xFF2563EB), fontSize: 10, fontStyle: FontStyle.italic))),
            ]),
          ),
          const SizedBox(height: 6),
          TextButton.icon(
            onPressed: () => _printIssue(iss['id'].toString()),
            icon: const Icon(Icons.print, size: 16), label: const Text('Print Form', style: TextStyle(fontSize: 12))),
        ]));
  }

  Future<void> _printIssue(String id) async {
    try {
      final url = await ApiService.printUrl(_p ? 'peripheral-issue' : 'store-issue', id);
      final uri = Uri.parse(url);
      final ok = await launchUrl(uri, mode: LaunchMode.externalApplication);
      if (!ok && mounted) ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Could not open browser')));
    } catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e')));
    }
  }

  void _showItemDetail(Map<String, dynamic> it) {
    final avail = _avail(it);
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
          Text((it['name'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18)),
          Text('${it[_typeKey] ?? ''} · ${it['brand'] ?? ''}', style: const TextStyle(color: Colors.grey, fontSize: 13)),
          const SizedBox(height: 12),
          Row(children: [
            _qtyBadge('Total', it['totalQty'], const Color(0xFF374151)),
            const SizedBox(width: 8),
            _qtyBadge('Issued', it['issuedQty'], const Color(0xFFD97706)),
            const SizedBox(width: 8),
            _qtyBadge('Damaged', it['damagedQty'], const Color(0xFFDC2626)),
            const SizedBox(width: 8),
            _qtyBadge('Avail', avail, const Color(0xFF059669)),
          ]),
          const SizedBox(height: 12),
          if ((it['specs'] ?? '').toString().isNotEmpty) Text('Specs: ${it['specs']}', style: const TextStyle(fontSize: 12, color: Colors.grey)),
          if ((it['location'] ?? '').toString().isNotEmpty) Text('Location: ${it['location']}', style: const TextStyle(fontSize: 12, color: Colors.grey)),
          const SizedBox(height: 20),
          Row(children: [
            Expanded(child: OutlinedButton.icon(
              onPressed: () { Navigator.pop(ctx); _showStockIn(it); },
              icon: const Icon(Icons.add_box), label: const Text('Stock In'))),
            const SizedBox(width: 10),
            Expanded(child: ElevatedButton.icon(
              onPressed: avail <= 0 ? null : () { Navigator.pop(ctx); _showIssue(it); },
              icon: const Icon(Icons.outbox), label: const Text('Issue'),
              style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF2563EB), foregroundColor: Colors.white))),
          ]),
        ])),
      ),
    );
  }

  void _showAddMenu() {
    showModalBottomSheet(context: context, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => SafeArea(child: Column(mainAxisSize: MainAxisSize.min, children: [
        Row(children: [
          const Padding(padding: EdgeInsets.only(left: 16), child: SizedBox()),
          const Spacer(),
          IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx)),
        ]),
        ListTile(leading: const Icon(Icons.add_circle, color: Color(0xFF059669)), title: const Text('Add New Item'),
          onTap: () { Navigator.pop(ctx); _showAddItem(); }),
        ListTile(leading: const Icon(Icons.add_box, color: Color(0xFF2563EB)), title: const Text('Stock In (existing item)'),
          onTap: () { Navigator.pop(ctx); _pickItemThen(_showStockIn); }),
        ListTile(leading: const Icon(Icons.outbox, color: Color(0xFFD97706)), title: const Text('Issue Item'),
          onTap: () { Navigator.pop(ctx); _pickItemThen(_showIssue); }),
      ])));
  }

  void _pickItemThen(void Function(Map<String, dynamic>) action) {
    if (_items.isEmpty) { ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Add an item first!'))); return; }
    showModalBottomSheet(context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => SizedBox(height: MediaQuery.of(ctx).size.height * .6,
        child: Column(children: [
          Row(children: [
            const Padding(padding: EdgeInsets.only(left: 16, top: 8), child: Text('Select Item', style: TextStyle(fontWeight: FontWeight.bold))),
            const Spacer(),
            IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx)),
          ]),
          Expanded(child: ListView.builder(padding: const EdgeInsets.all(12), itemCount: _items.length,
            itemBuilder: (_, i) { final it = _items[i]; return ListTile(
              title: Text((it['name'] ?? '').toString()),
              subtitle: Text('${it[_typeKey] ?? ''}  |  Avail: ${_avail(it)}'),
              onTap: () { Navigator.pop(ctx); action(it); },
            ); })),
        ])));
  }

  void _showAddItem() {
    final name = TextEditingController();
    final brand = TextEditingController();
    final model = TextEditingController();
    final specs = TextEditingController();
    final location = TextEditingController(text: _p ? 'IT Store' : 'IT Store');
    final notes = TextEditingController();
    final openQty = TextEditingController(text: '0');
    final rate = TextEditingController(text: '0');
    final minStock = TextEditingController(text: '2');
    String type = _typeOptions[0];
    bool saving = false;

    showModalBottomSheet(context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => StatefulBuilder(builder: (ctx, setS) => Padding(
        padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(ctx).viewInsets.bottom + 16),
        child: SingleChildScrollView(child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            const Expanded(child: Text('New Item', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 18))),
            IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
          ]),
          const SizedBox(height: 12),
          DropdownButtonFormField<String>(value: type, isExpanded: true,
            items: _typeOptions.map((t) => DropdownMenuItem(value: t, child: Text(t))).toList(),
            onChanged: (v) => setS(() => type = v!),
            decoration: InputDecoration(labelText: _typeLabel, border: const OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: name, decoration: const InputDecoration(labelText: 'Name *', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: brand, decoration: const InputDecoration(labelText: 'Brand', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: model, decoration: const InputDecoration(labelText: 'Model', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: specs, maxLines: 2, decoration: const InputDecoration(labelText: 'Specs', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          Row(children: [
            Expanded(child: TextField(controller: openQty, keyboardType: TextInputType.number,
              decoration: const InputDecoration(labelText: 'Opening Qty', border: OutlineInputBorder()))),
            const SizedBox(width: 8),
            Expanded(child: TextField(controller: rate, keyboardType: TextInputType.number,
              decoration: const InputDecoration(labelText: 'Rate/Piece (₹)', border: OutlineInputBorder()))),
          ]),
          const SizedBox(height: 10),
          Row(children: [
            Expanded(child: TextField(controller: minStock, keyboardType: TextInputType.number,
              decoration: const InputDecoration(labelText: 'Min Stock Alert', border: OutlineInputBorder()))),
            const SizedBox(width: 8),
            Expanded(child: TextField(controller: location, decoration: const InputDecoration(labelText: 'Location', border: OutlineInputBorder()))),
          ]),
          const SizedBox(height: 10),
          TextField(controller: notes, decoration: const InputDecoration(labelText: 'Notes', border: OutlineInputBorder())),
          const SizedBox(height: 16),
          SizedBox(width: double.infinity, height: 48, child: ElevatedButton(
            onPressed: saving ? null : () async {
              if (name.text.trim().isEmpty) { ScaffoldMessenger.of(ctx).showSnackBar(const SnackBar(content: Text('Name required!'))); return; }
              setS(() => saving = true);
              try {
                final item = {
                  _typeKey: type, 'name': name.text.trim(), 'brand': brand.text.trim(), 'model': model.text.trim(),
                  'specs': specs.text.trim(), 'location': location.text.trim(), 'notes': notes.text.trim(),
                  'minStock': int.tryParse(minStock.text.trim()) ?? 2,
                };
                final oq = int.tryParse(openQty.text.trim()) ?? 0;
                final rt = double.tryParse(rate.text.trim()) ?? 0;
                if (_p) {
                  await ApiService.savePeripheralItem(item, openingQty: oq, openingRate: rt);
                } else {
                  await ApiService.saveStoreItem(item, openingQty: oq, openingRate: rt);
                }
                if (ctx.mounted) Navigator.pop(ctx);
                if (mounted) ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(content: Text('Item added!'), backgroundColor: Color(0xFF059669)));
                _load();
              } catch (e) {
                setS(() => saving = false);
                if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
              }
            },
            style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF059669), foregroundColor: Colors.white),
            child: saving
              ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
              : const Text('Add Item', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 15)),
          )),
          const SizedBox(height: 8),
        ])),
      )));
  }

  void _showStockIn(Map<String, dynamic> it) {
    final qty = TextEditingController(text: '1');
    final vendor = TextEditingController();
    final invoice = TextEditingController();
    final rate = TextEditingController(text: '0');
    final source = TextEditingController(text: 'Purchase');
    final remarks = TextEditingController();
    bool saving = false;

    showModalBottomSheet(context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => StatefulBuilder(builder: (ctx, setS) => Padding(
        padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(ctx).viewInsets.bottom + 16),
        child: SingleChildScrollView(child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text('Stock In — ${it['name']}', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16))),
            IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
          ]),
          const SizedBox(height: 12),
          TextField(controller: qty, keyboardType: TextInputType.number,
            decoration: const InputDecoration(labelText: 'Quantity *', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: vendor, decoration: const InputDecoration(labelText: 'Vendor', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: invoice, decoration: const InputDecoration(labelText: 'Invoice No', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: rate, keyboardType: TextInputType.number,
            decoration: const InputDecoration(labelText: 'Rate/Piece (₹)', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: source, decoration: const InputDecoration(labelText: 'Source', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: remarks, decoration: const InputDecoration(labelText: 'Remarks', border: OutlineInputBorder())),
          const SizedBox(height: 16),
          SizedBox(width: double.infinity, height: 48, child: ElevatedButton(
            onPressed: saving ? null : () async {
              final q = int.tryParse(qty.text.trim()) ?? 0;
              if (q <= 0) { ScaffoldMessenger.of(ctx).showSnackBar(const SnackBar(content: Text('Enter a quantity!'))); return; }
              setS(() => saving = true);
              final data = {'qty': q, 'vendor': vendor.text.trim(), 'invoiceNo': invoice.text.trim(),
                'rate': double.tryParse(rate.text.trim()) ?? 0, 'source': source.text.trim(),
                'remarks': remarks.text.trim(), 'addedBy': 'Mobile Admin'};
              try {
                if (_p) { await ApiService.stockInPeripheral(it['id'].toString(), data); }
                else { await ApiService.stockInStoreItem(it['id'].toString(), data); }
                if (ctx.mounted) Navigator.pop(ctx);
                if (mounted) ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(content: Text('$q units added!'), backgroundColor: const Color(0xFF059669)));
                _load();
              } catch (e) {
                setS(() => saving = false);
                if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
              }
            },
            style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF2563EB), foregroundColor: Colors.white),
            child: saving
              ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
              : const Text('Add Stock', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 15)),
          )),
          const SizedBox(height: 8),
        ])),
      )));
  }

  Future<void> _showIssue(Map<String, dynamic> it) async {
    List<Map<String, dynamic>> emps = [];
    try { emps = await ApiService.getEmployeesFull(); } catch (_) {}
    if (!mounted) return;
    Map<String, dynamic>? selectedEmp;
    final qty = TextEditingController(text: '1');
    final reason = TextEditingController();
    final search = TextEditingController();
    List<Map<String, dynamic>> filtered = emps;
    bool saving = false;
    final avail = _avail(it);

    showModalBottomSheet(context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => StatefulBuilder(builder: (ctx, setS) => Padding(
        padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(ctx).viewInsets.bottom + 16),
        child: SizedBox(
          height: MediaQuery.of(ctx).size.height * .78,
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Row(children: [
              Expanded(child: Text('Issue — ${it['name']}  (Avail: $avail)', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16))),
              IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
            ]),
            const SizedBox(height: 10),
            TextField(controller: qty, keyboardType: TextInputType.number,
              decoration: const InputDecoration(labelText: 'Quantity *', border: OutlineInputBorder(), isDense: true)),
            const SizedBox(height: 10),
            TextField(controller: reason,
              decoration: InputDecoration(labelText: _reasonLabel, border: const OutlineInputBorder(), isDense: true)),
            const SizedBox(height: 10),
            if (selectedEmp != null)
              Container(padding: const EdgeInsets.all(10),
                decoration: BoxDecoration(color: const Color(0xFFF0FDF4), borderRadius: BorderRadius.circular(8)),
                child: Row(children: [
                  Expanded(child: Text('${selectedEmp!['name']} (${selectedEmp!['empId']}) · ${selectedEmp!['dept']}',
                      style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 12))),
                  IconButton(icon: const Icon(Icons.close, size: 18), onPressed: () => setS(() => selectedEmp = null)),
                ]))
            else ...[
              TextField(controller: search, onChanged: (v) => setS(() {
                  final q = v.toLowerCase();
                  filtered = emps.where((e) => (e['name'] ?? '').toString().toLowerCase().contains(q) ||
                    (e['empId'] ?? '').toString().toLowerCase().contains(q)).toList();
                }),
                decoration: const InputDecoration(labelText: 'Search employee *', border: OutlineInputBorder(), isDense: true)),
              const SizedBox(height: 6),
              Expanded(child: ListView.builder(itemCount: filtered.length, itemBuilder: (_, i) {
                final e = filtered[i];
                return ListTile(dense: true, title: Text((e['name'] ?? '').toString()),
                  subtitle: Text('${e['empId']} · ${e['dept']}'),
                  onTap: () => setS(() => selectedEmp = e));
              })),
            ],
            const SizedBox(height: 10),
            SizedBox(width: double.infinity, height: 48, child: ElevatedButton(
              onPressed: saving ? null : () async {
                final q = int.tryParse(qty.text.trim()) ?? 0;
                if (q <= 0) { ScaffoldMessenger.of(ctx).showSnackBar(const SnackBar(content: Text('Enter a quantity!'))); return; }
                if (selectedEmp == null) { ScaffoldMessenger.of(ctx).showSnackBar(const SnackBar(content: Text('Select an employee!'))); return; }
                setS(() => saving = true);
                final data = {'itemId': it['id'], 'qty': q, 'empId': selectedEmp!['empId'],
                  _reasonKey: reason.text.trim(), 'issuedBy': 'IT Admin', 'remarks': ''};
                try {
                  if (_p) { await ApiService.issuePeripheral(data); }
                  else { await ApiService.issueStoreItem(data); }
                  if (ctx.mounted) Navigator.pop(ctx);
                  if (mounted) ScaffoldMessenger.of(context).showSnackBar(
                      const SnackBar(content: Text('Issue created!'), backgroundColor: Color(0xFF059669)));
                  _load();
                } catch (e) {
                  setS(() => saving = false);
                  if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
                }
              },
              style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFFD97706), foregroundColor: Colors.white),
              child: saving
                ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                : const Text('Issue Item', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 15)),
            )),
          ]),
        ),
      )));
  }

  void _showReturnDialog(Map<String, dynamic> iss) {
    String condition = 'Good';
    final remarks = TextEditingController();
    bool saving = false;
    showModalBottomSheet(context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => StatefulBuilder(builder: (ctx, setS) => Padding(
        padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(ctx).viewInsets.bottom + 16),
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text('Return — ${iss['itemName']}', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16))),
            IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
          ]),
          Text('${iss['empName']} · Qty: ${iss['qty']}', style: const TextStyle(color: Colors.grey, fontSize: 12)),
          const SizedBox(height: 12),
          DropdownButtonFormField<String>(value: condition, isExpanded: true,
            items: ['Good','Damaged','Partially Damaged'].map((c) => DropdownMenuItem(value: c, child: Text(c))).toList(),
            onChanged: (v) => setS(() => condition = v!),
            decoration: const InputDecoration(labelText: 'Condition', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: remarks, decoration: const InputDecoration(labelText: 'Remarks', border: OutlineInputBorder())),
          const SizedBox(height: 16),
          SizedBox(width: double.infinity, height: 48, child: ElevatedButton(
            onPressed: saving ? null : () async {
              setS(() => saving = true);
              try {
                final data = {'condition': condition, 'remarks': remarks.text.trim()};
                if (_p) { await ApiService.returnPeripheralIssue(iss['id'].toString(), data); }
                else { await ApiService.returnStoreIssue(iss['id'].toString(), data); }
                if (ctx.mounted) Navigator.pop(ctx);
                if (mounted) ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(content: Text('Return processed!'), backgroundColor: Color(0xFF059669)));
                _load();
              } catch (e) {
                setS(() => saving = false);
                if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
              }
            },
            style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF059669), foregroundColor: Colors.white),
            child: saving
              ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
              : const Text('Process Return', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 15)),
          )),
          const SizedBox(height: 8),
        ]),
      )));
  }
}
