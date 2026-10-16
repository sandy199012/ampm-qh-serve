import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';
import '../services/api_service.dart';
import '../app_theme.dart';
import 'home_screen.dart';
import 'admin_home_screen.dart';

// Shown right after a first-time mobile login (initial password = employee
// code, set by Provision Mobile Logins) or any time from Settings. Forces a
// new password before the user can continue into the app.
class ChangePasswordScreen extends StatefulWidget {
  final bool forced;
  const ChangePasswordScreen({super.key, this.forced = true});
  @override
  State<ChangePasswordScreen> createState() => _ChangePasswordScreenState();
}

class _ChangePasswordScreenState extends State<ChangePasswordScreen> {
  final _pwd1Ctrl = TextEditingController();
  final _pwd2Ctrl = TextEditingController();
  bool _saving = false, _obscure1 = true, _obscure2 = true;
  String? _error;

  Future<void> _save() async {
    final p1 = _pwd1Ctrl.text.trim();
    final p2 = _pwd2Ctrl.text.trim();
    if (p1.length < 4) { setState(() => _error = 'Password must be at least 4 characters'); return; }
    if (p1 != p2) { setState(() => _error = 'Passwords do not match'); return; }

    setState(() { _saving = true; _error = null; });
    final ok = await ApiService.changePassword(p1);
    if (!ok) {
      setState(() { _saving = false; _error = 'Could not update password. Try again.'; });
      return;
    }
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString('password', p1);
    setState(() => _saving = false);

    if (!mounted) return;
    if (widget.forced) {
      final isAdmin = prefs.getBool('is_admin') ?? false;
      Navigator.pushReplacement(context,
          MaterialPageRoute(builder: (_) => isAdmin ? const AdminHomeScreen() : const HomeScreen()));
    } else {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Password updated.')));
      Navigator.pop(context);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: widget.forced ? null : AppBar(title: const Text('Change Password')),
      body: Container(
        decoration: widget.forced ? const BoxDecoration(gradient: AppColors.navyGradient) : null,
        child: SafeArea(
          child: SingleChildScrollView(
            padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 36),
            child: Column(children: [
              if (widget.forced) ...[
                const SizedBox(height: 12),
                const AppLogoBadge(size: 64),
                const SizedBox(height: 18),
                Text('Set a New Password',
                    style: TextStyle(color: widget.forced ? Colors.white : null, fontSize: 22, fontWeight: FontWeight.bold)),
                const SizedBox(height: 6),
                Text('This is your first login — please set your own password to continue.',
                    textAlign: TextAlign.center,
                    style: TextStyle(color: widget.forced ? Colors.white.withOpacity(.65) : Colors.black54, fontSize: 13)),
                const SizedBox(height: 30),
              ],
              Container(
                padding: const EdgeInsets.all(22),
                decoration: widget.forced
                    ? BoxDecoration(
                        color: Colors.white.withOpacity(.07),
                        borderRadius: BorderRadius.circular(22),
                        border: Border.all(color: Colors.white.withOpacity(.14)),
                      )
                    : null,
                child: Column(children: [
                  _field(label: 'New Password', controller: _pwd1Ctrl, obscure: _obscure1,
                      onToggle: () => setState(() => _obscure1 = !_obscure1)),
                  const SizedBox(height: 16),
                  _field(label: 'Confirm New Password', controller: _pwd2Ctrl, obscure: _obscure2,
                      onToggle: () => setState(() => _obscure2 = !_obscure2), onSubmit: (_) => _save()),
                  if (_error != null) ...[
                    const SizedBox(height: 14),
                    Container(
                      width: double.infinity,
                      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                      decoration: BoxDecoration(
                        color: AppColors.red.withOpacity(.15),
                        borderRadius: BorderRadius.circular(10),
                        border: Border.all(color: AppColors.red.withOpacity(.35)),
                      ),
                      child: Row(children: [
                        const Icon(Icons.error_outline_rounded, color: Color(0xFFFCA5A5), size: 17),
                        const SizedBox(width: 8),
                        Expanded(child: Text(_error!, style: const TextStyle(color: Color(0xFFFCA5A5), fontSize: 12.5))),
                      ]),
                    ),
                  ],
                  const SizedBox(height: 22),
                  SizedBox(
                    width: double.infinity,
                    height: 52,
                    child: ElevatedButton(
                      onPressed: _saving ? null : _save,
                      style: ElevatedButton.styleFrom(
                          backgroundColor: AppColors.blue,
                          foregroundColor: Colors.white,
                          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14))),
                      child: _saving
                          ? const SizedBox(width: 22, height: 22,
                              child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                          : const Text('Save & Continue', style: TextStyle(fontSize: 16, fontWeight: FontWeight.bold)),
                    ),
                  ),
                ]),
              ),
            ]),
          ),
        ),
      ),
    );
  }

  Widget _field({
    required String label,
    required TextEditingController controller,
    required bool obscure,
    required VoidCallback onToggle,
    void Function(String)? onSubmit,
  }) {
    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      Text(label,
          style: TextStyle(color: widget.forced ? Colors.white.withOpacity(.6) : Colors.black54,
              fontSize: 12, fontWeight: FontWeight.w600, letterSpacing: .3)),
      const SizedBox(height: 8),
      TextField(
        controller: controller,
        obscureText: obscure,
        onSubmitted: onSubmit,
        style: TextStyle(color: widget.forced ? Colors.white : Colors.black87, fontSize: 15),
        decoration: InputDecoration(
          filled: true,
          fillColor: widget.forced ? Colors.white.withOpacity(.08) : Colors.black.withOpacity(.04),
          border: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: BorderSide.none),
          prefixIcon: Icon(Icons.lock_outline_rounded, color: AppColors.cyan, size: 20),
          suffixIcon: IconButton(
            icon: Icon(obscure ? Icons.visibility_outlined : Icons.visibility_off_outlined,
                color: widget.forced ? Colors.white38 : Colors.black38, size: 20),
            onPressed: onToggle,
          ),
          contentPadding: const EdgeInsets.symmetric(vertical: 14),
        ),
      ),
    ]);
  }
}
