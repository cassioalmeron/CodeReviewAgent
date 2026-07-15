// Centralized SVG icons (per REACT-REFERENCE.md). Add new icons here as named exports,
// each taking IconProps and defaulting color to currentColor so they inherit text color.
export interface IconProps {
  size?: number
  color?: string
  className?: string
}

export function DiamondIcon({ size = 24, color = 'currentColor', className }: IconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill={color}
      className={className}
      aria-hidden="true"
    >
      <path d="M12 2 22 12 12 22 2 12z" />
    </svg>
  )
}
