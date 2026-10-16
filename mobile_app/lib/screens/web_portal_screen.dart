import 'dart:convert';
import 'dart:typed_data';
import 'package:flutter/material.dart';
import 'package:webview_flutter/webview_flutter.dart';
import 'package:shared_preferences/shared_preferences.dart';
import '../app_theme.dart';

// For superadmin: the entire AMPM IT Tool website inside a WebView, with
// auto-login. This means any module — Assets, IT Store, Purchase Orders,
// Vendors, Budget, Bills, Licenses, Goals, or anything added to the website
// later — becomes available on mobile instantly, without rebuilding/
// reinstalling the APK. The website's own navbar (hamburger menu) provides
// navigation.
class WebPortalScreen extends StatefulWidget {
  final String path;   // e.g. '/', '/Assets/Index', '/ITStore/Index'
  final String title;
  const WebPortalScreen({super.key, this.path = '/', this.title = 'AMPM IT Tool'});

  @override
  State<WebPortalScreen> createState() => _WebPortalScreenState();
}

class _WebPortalScreenState extends State<WebPortalScreen> {
  static const String _baseUrl = 'https://ampm-qh-serve-1.onrender.com';
  late final WebViewController _controller;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _controller = WebViewController()
      ..setJavaScriptMode(JavaScriptMode.unrestricted)
      ..setBackgroundColor(AppColors.bg)
      ..setNavigationDelegate(NavigationDelegate(
        onPageFinished: (_) { if (mounted) setState(() => _loading = false); },
        onWebResourceError: (err) {
          if (mounted) setState(() { _loading = false; _error = err.description; });
        },
      ));
    _loginAndOpen();
  }

  Future<void> _loginAndOpen() async {
    final prefs = await SharedPreferences.getInstance();
    final u = prefs.getString('username') ?? '';
    final p = prefs.getString('password') ?? '';
    final formBody = 'username=${Uri.encodeComponent(u)}&password=${Uri.encodeComponent(p)}';
    try {
      // Auto-login: same form POST the website's own login page submits.
      // The server sets its session cookies on the redirect response, which
      // this WebView keeps for every page opened afterwards.
      await _controller.loadRequest(
        Uri.parse('$_baseUrl/Account/Login'),
        method: LoadRequestMethod.post,
        headers: const {'Content-Type': 'application/x-www-form-urlencoded'},
        body: Uint8List.fromList(utf8.encode(formBody)),
      );
    } catch (e) {
      if (mounted) setState(() { _loading = false; _error = e.toString(); });
    }
  }

  Future<bool> _onWillPop() async {
    if (await _controller.canGoBack()) { _controller.goBack(); return false; }
    return true;
  }

  @override
  Widget build(BuildContext context) {
    return WillPopScope(
      onWillPop: _onWillPop,
      child: Scaffold(
        backgroundColor: AppColors.bg,
        appBar: AppBar(
          backgroundColor: AppColors.navy,
          title: Text(widget.title, style: const TextStyle(color: Colors.white, fontSize: 15)),
          actions: [
            IconButton(icon: const Icon(Icons.refresh, color: Colors.white),
                onPressed: () => _controller.reload()),
          ],
        ),
        body: Stack(children: [
          if (_error == null) WebViewWidget(controller: _controller),
          if (_error != null)
            Center(child: Padding(padding: const EdgeInsets.all(24), child: Column(
              mainAxisAlignment: MainAxisAlignment.center, children: [
                const Icon(Icons.wifi_off, size: 56, color: Color(0xFFDC2626)),
                const SizedBox(height: 12),
                const Text('Website failed to load', style: TextStyle(fontWeight: FontWeight.bold)),
                const SizedBox(height: 6),
                Text(_error ?? '', style: const TextStyle(color: Colors.grey, fontSize: 12), textAlign: TextAlign.center),
                const SizedBox(height: 16),
                ElevatedButton.icon(
                  onPressed: () { setState(() { _error = null; _loading = true; }); _loginAndOpen(); },
                  icon: const Icon(Icons.refresh), label: const Text('Retry'),
                ),
              ]))),
          if (_loading && _error == null)
            const Center(child: CircularProgressIndicator(color: AppColors.cyan)),
        ]),
      ),
    );
  }
}
