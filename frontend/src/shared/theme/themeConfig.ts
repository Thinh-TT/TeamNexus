import type { ThemeConfig } from 'antd'

/**
 * TeamNexus Design System: "Intelligent Orchestration"
 * Generated & verified via Stitch MCP (Project ID: 691778267974237877)
 * Light Mode primary palette with Electric Indigo, Slate-50 Canvas, and Emerald accents.
 */
export const teamNexusTheme: ThemeConfig = {
  token: {
    colorPrimary: '#4f46e5', // Electric Indigo
    colorInfo: '#06b6d4', // Cyan
    colorSuccess: '#10b981', // Emerald
    colorWarning: '#f59e0b', // Warm Amber
    colorError: '#ef4444', // Rose Red
    colorBgBase: '#f8fafc', // Slate 50
    colorTextBase: '#0f172a', // Slate 900
    colorTextSecondary: '#475569', // Slate 600
    colorBorder: '#e2e8f0', // Slate 200
    colorBorderSecondary: '#f1f5f9', // Slate 100
    borderRadius: 8,
    borderRadiusLG: 12,
    borderRadiusSM: 6,
    fontFamily:
      "'Plus Jakarta Sans', 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif",
    fontSize: 14,
    fontSizeHeading1: 32,
    fontSizeHeading2: 24,
    fontSizeHeading3: 18,
    fontSizeHeading4: 16,
    fontSizeHeading5: 14,
    lineHeight: 1.5,
  },
  components: {
    Button: {
      controlHeight: 38,
      fontWeight: 600,
      borderRadius: 8,
      primaryShadow: '0 2px 8px rgba(79, 70, 229, 0.2)',
    },
    Card: {
      borderRadiusLG: 12,
      paddingLG: 20,
    },
    Input: {
      controlHeight: 38,
      borderRadius: 8,
      colorBorder: '#cbd5e1',
    },
    Select: {
      controlHeight: 38,
      borderRadius: 8,
    },
    Tag: {
      borderRadius: 6,
      fontSize: 12,
      lineHeight: 1.6,
    },
    Modal: {
      borderRadiusLG: 16,
    },
    Drawer: {
      borderRadiusLG: 0,
    },
    Menu: {
      itemBorderRadius: 8,
      itemSelectedColor: '#4f46e5',
      itemSelectedBg: '#eef2ff',
    },
    Table: {
      borderRadius: 10,
    },
  },
}
