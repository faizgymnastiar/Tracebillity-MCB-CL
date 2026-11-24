export const ROLE_PRESETS = [
  { label: 'Super Admin', permissions: ['admin.accounts', 'admin.master', 'scan.unit', 'ng.record', 'scan.inner', 'scan.outer', 'print.manage', 'history.read'] },
  { label: 'Admin Akun', permissions: ['admin.accounts'] },
  { label: 'Admin Master', permissions: ['admin.master'] },
  { label: 'Operator Scan OK', permissions: ['scan.unit'] },
  { label: 'Operator NG & Rework', permissions: ['ng.record'] },
  { label: 'Operator Inner', permissions: ['scan.inner'] },
  { label: 'Operator Outer', permissions: ['scan.outer'] },
  { label: 'Operator Produksi Lengkap', permissions: ['scan.unit', 'ng.record', 'scan.inner', 'scan.outer'] },
  { label: 'Petugas Label', permissions: ['print.manage'] },
  { label: 'Pembaca Riwayat', permissions: ['history.read'] },
];

export function matchingRole(permissions: string[]) {
  return ROLE_PRESETS.find((role) => role.permissions.length === permissions.length &&
    role.permissions.every((permission) => permissions.includes(permission)));
}
