import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';
import '../app_theme.dart';
import '../services/update_service.dart';
import '../widgets/update_ui.dart';

/// "App & Updates": installed version, a manual "Check for updates" button and
/// the link to the latest APK (handy to share a new colleague's first install).
class AboutScreen extends StatefulWidget {
  const AboutScreen({super.key});
  @override
  State<AboutScreen> createState() => _AboutScreenState();
}

class _AboutScreenState extends State<AboutScreen> {
  bool _checking = false;
  String _status = '';
  Color _statusColor = AppColors.textMuted;

  @override
  void initState() {
    super.initState();
    if (UpdateService.currentVersion.isEmpty) {
      UpdateService.loadCurrent().then((_) { if (mounted) setState(() {}); });
    }
  }

  Future<void> _check() async {
    setState(() { _checking = true; _status = ''; });
    try {
      final info = await UpdateService.check(force: true);
      if (!mounted) return;
      if (info == null) {
        setState(() {
          _checking = false;
          _status = 'Aapka app latest version par hai ✓';
          _statusColor = AppColors.green;
        });
      } else {
        setState(() {
          _checking = false;
          _status = 'Naya version ${info.version} available hai';
          _statusColor = AppColors.blue;
        });
        await UpdateUi.showUpdateDialog(context, info);
      }
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _checking = false;
        _status = 'Check nahi ho paya — internet dekho aur dobara try karo.';
        _statusColor = AppColors.red;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final v = UpdateService.currentVersion;
    final b = UpdateService.currentBuild;
    return Scaffold(
      backgroundColor: AppColors.bg,
      appBar: AppBar(title: const Text('App & Updates')),
      body: ListView(padding: const EdgeInsets.all(20), children: [
        Container(
          padding: const EdgeInsets.symmetric(vertical: 28, horizontal: 20),
          decoration: BoxDecoration(
            gradient: AppColors.navyGradient,
            borderRadius: BorderRadius.circular(22),
            boxShadow: AppShadows.soft,
          ),
          child: Column(children: [
            const AppLogoBadge(size: 72),
            const SizedBox(height: 16),
            const Text('AMPM IT Helpdesk',
                style: TextStyle(color: Colors.white, fontSize: 20, fontWeight: FontWeight.bold)),
            const SizedBox(height: 4),
            Text('AMPM Fashions Pvt. Ltd.',
                style: TextStyle(color: Colors.white.withOpacity(.65), fontSize: 12.5)),
            const SizedBox(height: 14),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 6),
              decoration: BoxDecoration(color: Colors.white.withOpacity(.12), borderRadius: BorderRadius.circular(20)),
              child: Text(v.isEmpty ? 'Version —' : 'Version $v${b.isEmpty ? '' : '  (build $b)'}',
                  style: const TextStyle(color: AppColors.cyan, fontWeight: FontWeight.w600, fontSize: 13)),
            ),
          ]),
        ),
        const SizedBox(height: 18),
        if (UpdateService.supported)
          ElevatedButton.icon(
            onPressed: _checking ? null : _check,
            icon: _checking
                ? const SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                : const Icon(Icons.system_update_rounded),
            label: Text(_checking ? 'Check ho raha hai…' : 'Update check karo'),
            style: ElevatedButton.styleFrom(backgroundColor: AppColors.blue, foregroundColor: Colors.white),
          ),
        if (_status.isNotEmpty) ...[
          const SizedBox(height: 12),
          Center(child: Text(_status,
              textAlign: TextAlign.center,
              style: TextStyle(color: _statusColor, fontWeight: FontWeight.w600, fontSize: 13))),
        ],
        const SizedBox(height: 18),
        Container(
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(
            color: Colors.white,
            borderRadius: BorderRadius.circular(18),
            border: Border.all(color: AppColors.cardBorder),
          ),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            const Text('Updates kaise milte hain?',
                style: TextStyle(fontWeight: FontWeight.bold, color: AppColors.textDark)),
            const SizedBox(height: 6),
            const Text(
              'Jab bhi IT naya version release karta hai, app kholte hi "Naya update available" '
              'dikhta hai. "Update karo" dabao — app download karke install screen kholti hai, '
              'bas ek baar "Install" dabana hota hai. Purana app delete ya dobara install karne ki zaroorat nahi.',
              style: TextStyle(fontSize: 12.5, height: 1.45, color: AppColors.textMuted),
            ),
            const SizedBox(height: 10),
            TextButton.icon(
              onPressed: () => launchUrl(Uri.parse(UpdateService.downloadPage), mode: LaunchMode.externalApplication),
              icon: const Icon(Icons.open_in_new_rounded, size: 16),
              label: const Text('Latest APK page kholo'),
              style: TextButton.styleFrom(foregroundColor: AppColors.blue, padding: EdgeInsets.zero),
            ),
          ]),
        ),
      ]),
    );
  }
}
