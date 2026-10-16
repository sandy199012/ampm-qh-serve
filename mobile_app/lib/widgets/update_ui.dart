import 'dart:io';
import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';
import '../app_theme.dart';
import '../services/update_service.dart';

/// Everything the user sees about app updates: the "new version" dialog with a
/// progress bar, the banner on the home screens, and the automatic check when
/// the app opens or comes back to the front.
class UpdateUi {
  static bool _prompting = false;

  /// Silent check (limited to once every few hours); shows the dialog only if
  /// there is a newer version and the user has not snoozed it.
  static Future<void> checkAndPrompt(GlobalKey<NavigatorState> navKey) async {
    if (!UpdateService.supported || _prompting) return;
    final info = await UpdateService.check();
    if (info == null || !await UpdateService.shouldPrompt(info)) return;
    // give the home screen a moment to appear first
    await Future.delayed(const Duration(milliseconds: 800));
    final ctx = navKey.currentContext;
    if (ctx == null || _prompting || !ctx.mounted) return;
    _prompting = true;
    try {
      await showUpdateDialog(ctx, info);
    } finally {
      _prompting = false;
    }
  }

  static Future<void> showUpdateDialog(BuildContext context, UpdateInfo info) {
    return showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (_) => _UpdateDialog(info: info),
    );
  }
}

class _UpdateDialog extends StatefulWidget {
  final UpdateInfo info;
  const _UpdateDialog({required this.info});
  @override
  State<_UpdateDialog> createState() => _UpdateDialogState();
}

enum _Phase { idle, downloading, ready, failed }

class _UpdateDialogState extends State<_UpdateDialog> {
  _Phase _phase = _Phase.idle;
  double _progress = 0;
  String _error = '';
  File? _file;

  UpdateInfo get info => widget.info;

  String get _sizeText => info.sizeBytes > 0
      ? '${(info.sizeBytes / (1024 * 1024)).toStringAsFixed(1)} MB'
      : '';

  Future<void> _start() async {
    setState(() { _phase = _Phase.downloading; _progress = 0; _error = ''; });
    try {
      final f = await UpdateService.download(info, (p) {
        if (mounted) setState(() => _progress = p);
      });
      _file = f;
      if (!mounted) return;
      await _install();
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _phase = _Phase.failed;
        _error = e.toString().replaceFirst('Exception: ', '');
      });
    }
  }

  Future<void> _install() async {
    final f = _file;
    if (f == null) return _start();
    final msg = await UpdateService.install(f);
    if (!mounted) return;
    setState(() {
      if (msg == null) {
        _phase = _Phase.ready;      // installer is open; if the user backs out, "Install karo" is still here
        _error = '';
      } else {
        _phase = _Phase.failed;
        _error = msg;
      }
    });
  }

  Future<void> _viaBrowser() async {
    try {
      await launchUrl(Uri.parse(info.apkUrl), mode: LaunchMode.externalApplication);
    } catch (_) {}
  }

  @override
  Widget build(BuildContext context) {
    final busy = _phase == _Phase.downloading;
    return PopScope(
      canPop: false,
      child: AlertDialog(
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(22)),
        titlePadding: const EdgeInsets.fromLTRB(22, 22, 22, 0),
        contentPadding: const EdgeInsets.fromLTRB(22, 14, 22, 0),
        title: Row(children: [
          Container(
            width: 44, height: 44,
            decoration: BoxDecoration(
              gradient: const LinearGradient(colors: [AppColors.blue, AppColors.cyan]),
              borderRadius: BorderRadius.circular(13),
            ),
            child: const Icon(Icons.system_update_rounded, color: Colors.white),
          ),
          const SizedBox(width: 12),
          Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(info.mandatory ? 'Zaroori update' : 'Naya update available',
                style: const TextStyle(fontSize: 17, fontWeight: FontWeight.bold, color: AppColors.textDark)),
            Text(
              'v${UpdateService.currentVersion}  →  v${info.version}'
              '${_sizeText.isEmpty ? '' : '   ·   $_sizeText'}',
              style: const TextStyle(fontSize: 12, color: AppColors.textMuted, fontWeight: FontWeight.w500),
            ),
          ])),
        ]),
        content: ConstrainedBox(
          constraints: const BoxConstraints(maxHeight: 300),
          child: SingleChildScrollView(
            child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
              if (info.notes.isNotEmpty) ...[
                const Text('Isme naya kya hai', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 13, color: AppColors.textDark)),
                const SizedBox(height: 6),
                Container(
                  width: double.infinity,
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(color: AppColors.bg, borderRadius: BorderRadius.circular(12)),
                  child: Text(info.notes, style: const TextStyle(fontSize: 12.5, height: 1.4, color: AppColors.textDark)),
                ),
              ] else
                const Text('Bug fixes aur improvements.', style: TextStyle(fontSize: 13, color: AppColors.textMuted)),
              if (info.mandatory) ...[
                const SizedBox(height: 10),
                const Text('Ye update install kiye bina app aage nahi chalegi.',
                    style: TextStyle(fontSize: 12, color: AppColors.red, fontWeight: FontWeight.w600)),
              ],
              if (busy) ...[
                const SizedBox(height: 16),
                ClipRRect(
                  borderRadius: BorderRadius.circular(8),
                  child: LinearProgressIndicator(
                    value: _progress > 0 ? _progress : null,
                    minHeight: 8,
                    color: AppColors.blue,
                    backgroundColor: AppColors.cardBorder,
                  ),
                ),
                const SizedBox(height: 6),
                Text(_progress > 0 ? 'Download ho raha hai… ${(_progress * 100).round()}%' : 'Shuru ho raha hai…',
                    style: const TextStyle(fontSize: 12, color: AppColors.textMuted)),
              ],
              if (_phase == _Phase.ready) ...[
                const SizedBox(height: 14),
                const Text('Install screen khul gayi hai — wahan "Install" dabao. '
                    'Install ke baad app apne aap naye version me khulegi.',
                    style: TextStyle(fontSize: 12.5, color: AppColors.green, fontWeight: FontWeight.w600)),
              ],
              if (_phase == _Phase.failed) ...[
                const SizedBox(height: 14),
                Text(_error, style: const TextStyle(fontSize: 12.5, color: AppColors.red, fontWeight: FontWeight.w600)),
              ],
              const SizedBox(height: 8),
            ]),
          ),
        ),
        actionsPadding: const EdgeInsets.fromLTRB(14, 6, 14, 14),
        actions: [
          if (!info.mandatory && !busy)
            TextButton(
              onPressed: () async {
                await UpdateService.snooze(info);
                if (context.mounted) Navigator.of(context).pop();
              },
              child: const Text('Baad me'),
            ),
          if (_phase == _Phase.failed)
            TextButton(onPressed: _viaBrowser, child: const Text('Browser se')),
          if (!busy)
            ElevatedButton.icon(
              onPressed: _phase == _Phase.ready || (_phase == _Phase.failed && _file != null)
                  ? _install
                  : _start,
              icon: Icon(_phase == _Phase.idle ? Icons.download_rounded : Icons.install_mobile_rounded, size: 18),
              label: Text(_phase == _Phase.idle
                  ? 'Update karo'
                  : (_phase == _Phase.failed && _file == null ? 'Dobara try karo' : 'Install karo')),
              style: ElevatedButton.styleFrom(
                backgroundColor: AppColors.blue,
                foregroundColor: Colors.white,
                padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 12),
              ),
            ),
        ],
      ),
    );
  }
}

/// Banner at the top of the home screens while a newer version is waiting.
class UpdateBanner extends StatelessWidget {
  const UpdateBanner({super.key});

  @override
  Widget build(BuildContext context) {
    return ValueListenableBuilder<UpdateInfo?>(
      valueListenable: UpdateService.available,
      builder: (context, info, _) {
        if (info == null) return const SizedBox.shrink();
        return Padding(
          padding: const EdgeInsets.only(bottom: 14),
          child: Material(
            color: Colors.transparent,
            child: InkWell(
              borderRadius: BorderRadius.circular(16),
              onTap: () => UpdateUi.showUpdateDialog(context, info),
              child: Ink(
                padding: const EdgeInsets.all(14),
                decoration: BoxDecoration(
                  gradient: const LinearGradient(colors: [AppColors.blue, AppColors.cyan]),
                  borderRadius: BorderRadius.circular(16),
                  boxShadow: [BoxShadow(color: AppColors.blue.withOpacity(.3), blurRadius: 14, offset: const Offset(0, 5))],
                ),
                child: Row(children: [
                  Container(
                    width: 38, height: 38,
                    decoration: BoxDecoration(color: Colors.white.withOpacity(.2), borderRadius: BorderRadius.circular(11)),
                    child: const Icon(Icons.system_update_rounded, color: Colors.white, size: 22),
                  ),
                  const SizedBox(width: 12),
                  Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Text('Naya version ${info.version} available',
                        style: const TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 14)),
                    const Text('Tap karke update karo', style: TextStyle(color: Colors.white70, fontSize: 11.5)),
                  ])),
                  const Icon(Icons.arrow_forward_ios_rounded, color: Colors.white, size: 15),
                ]),
              ),
            ),
          ),
        );
      },
    );
  }
}

/// Wraps the whole app: when the app comes back to the front (after being in
/// the background for a while) it checks for a new version again.
class UpdateLifecycleWatcher extends StatefulWidget {
  final GlobalKey<NavigatorState> navKey;
  final Widget child;
  const UpdateLifecycleWatcher({super.key, required this.navKey, required this.child});
  @override
  State<UpdateLifecycleWatcher> createState() => _UpdateLifecycleWatcherState();
}

class _UpdateLifecycleWatcherState extends State<UpdateLifecycleWatcher> with WidgetsBindingObserver {
  @override
  void initState() { super.initState(); WidgetsBinding.instance.addObserver(this); }

  @override
  void dispose() { WidgetsBinding.instance.removeObserver(this); super.dispose(); }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) UpdateUi.checkAndPrompt(widget.navKey);
  }

  @override
  Widget build(BuildContext context) => widget.child;
}
