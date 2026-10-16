import 'package:flutter/material.dart';

// Shared design tokens for the AMPM Helpdesk app — one place for colors,
// gradients and shadows so every screen stays visually consistent.
class AppColors {
  static const navy = Color(0xFF130A33);
  static const navyLight = Color(0xFF2B1766);
  static const cyan = Color(0xFFFF5C93);
  static const blue = Color(0xFF6D4AFF);
  static const green = Color(0xFF059669);
  static const amber = Color(0xFFD97706);
  static const red = Color(0xFFDC2626);
  static const purple = Color(0xFF14B8A6);
  static const bg = Color(0xFFF5F2FF);
  static const cardBorder = Color(0xFFE6E0F7);
  static const textDark = Color(0xFF1A1033);
  static const textMuted = Color(0xFF6E6890);

  // "Aurora" theme: midnight violet -> electric violet, with a rose accent.
  // (blue = electric violet, cyan = rose, purple = mint; names kept so every screen follows.)
  static const navyGradient = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [navy, navyLight, Color(0xFF4A2BC9)],
  );
  static const accentGradient = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [blue, cyan],
  );
}

class AppShadows {
  static List<BoxShadow> soft = [
    BoxShadow(color: const Color(0xFF6D4AFF).withOpacity(.10), blurRadius: 22, offset: const Offset(0, 8)),
  ];
  static List<BoxShadow> tight = [
    BoxShadow(color: const Color(0xFF6D4AFF).withOpacity(.08), blurRadius: 12, offset: const Offset(0, 4)),
  ];
}

// Reusable rounded "app icon" badge used on splash/login/headers.
class AppLogoBadge extends StatelessWidget {
  final double size;
  const AppLogoBadge({super.key, this.size = 72});
  @override
  Widget build(BuildContext context) {
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        gradient: const LinearGradient(
            colors: [AppColors.blue, AppColors.cyan], begin: Alignment.topLeft, end: Alignment.bottomRight),
        borderRadius: BorderRadius.circular(size * 0.28),
        boxShadow: [BoxShadow(color: AppColors.blue.withOpacity(.35), blurRadius: 20, offset: const Offset(0, 8))],
      ),
      child: Icon(Icons.headset_mic_rounded, color: Colors.white, size: size * 0.5),
    );
  }
}

// A polished stat tile used on both dashboards (Open / In Progress / Resolved).
class StatTile extends StatelessWidget {
  final String label;
  final int value;
  final Color color;
  final IconData icon;
  const StatTile({super.key, required this.label, required this.value, required this.color, required this.icon});

  @override
  Widget build(BuildContext context) {
    return Expanded(
      child: Container(
        padding: const EdgeInsets.symmetric(vertical: 16, horizontal: 10),
        decoration: BoxDecoration(
          color: Colors.white,
          borderRadius: BorderRadius.circular(20),
          border: Border.all(color: AppColors.cardBorder),
          boxShadow: AppShadows.tight,
        ),
        child: Column(children: [
          Container(
            width: 34,
            height: 34,
            decoration: BoxDecoration(color: color.withOpacity(.12), borderRadius: BorderRadius.circular(10)),
            child: Icon(icon, color: color, size: 18),
          ),
          const SizedBox(height: 10),
          Text(value.toString(),
              style: TextStyle(color: AppColors.textDark, fontSize: 20, fontWeight: FontWeight.bold)),
          const SizedBox(height: 2),
          Text(label, style: const TextStyle(color: AppColors.textMuted, fontSize: 10.5, fontWeight: FontWeight.w600),
              textAlign: TextAlign.center),
        ]),
      ),
    );
  }
}
