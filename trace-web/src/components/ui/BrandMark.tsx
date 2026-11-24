const DEFAULT_LOGO = '/LELCO Manufacturing White Logo.png';

interface BrandMarkProps {
  companyName: string;
  logoDataUrl?: string;
  size?: number;
}

export function BrandMark({ companyName, logoDataUrl, size = 42 }: BrandMarkProps) {
  return (
    <img
      className="brand-logo"
      src={logoDataUrl || DEFAULT_LOGO}
      alt={`Logo ${companyName}`}
      style={{ height: size }}
    />
  );
}
