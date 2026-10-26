import React, { useState } from "react";
import { ArtCatalog } from "../../presentation/visualAndLayout";

export interface ArtSlotProps {
  category: string;
  contentId: string;
  manifestPath?: string;
  className?: string;
  fallbackLabel?: string;
  aspectRatio?: string;
}

export const ArtSlot: React.FC<ArtSlotProps> = ({
  category,
  contentId,
  manifestPath = ArtCatalog.IVORY_MANIFEST,
  className = "",
  fallbackLabel = "",
  aspectRatio = "16/10",
}) => {
  const [imgError, setImgError] = useState(false);
  const slot = ArtCatalog.resolve(category, contentId, manifestPath);
  const webPath = String(slot.webPath ?? "");
  const primaryColor = String(slot.primary ?? "#8d8270");
  const secondaryColor = String(slot.secondary ?? "#faf5eb");
  const symbol = String((slot.symbol ?? fallbackLabel.slice(0, 2).toUpperCase()) || "◆");

  return (
    <div
      className={`relative overflow-hidden flex items-center justify-center border border-[#8d8270]/80 bg-[#e9dec9] shadow-inner ${className}`}
      style={{
        aspectRatio,
        background: `radial-gradient(circle at 50% 42%, ${secondaryColor} 0%, #e7dbc3 72%, #d5c5a7 100%)`,
      }}
    >
      {/* Subtle geometric astrolabe/engraving backdrop */}
      <svg
        className="absolute inset-0 w-full h-full opacity-20 pointer-events-none"
        viewBox="0 0 100 60"
        preserveAspectRatio="none"
      >
        <line x1="0" y1="15" x2="100" y2="15" stroke={primaryColor} strokeWidth="0.45" />
        <line x1="0" y1="30" x2="100" y2="30" stroke={primaryColor} strokeWidth="0.45" />
        <line x1="0" y1="45" x2="100" y2="45" stroke={primaryColor} strokeWidth="0.45" />
        <line x1="25" y1="0" x2="25" y2="60" stroke={primaryColor} strokeWidth="0.45" />
        <line x1="50" y1="0" x2="50" y2="60" stroke={primaryColor} strokeWidth="0.45" />
        <line x1="75" y1="0" x2="75" y2="60" stroke={primaryColor} strokeWidth="0.45" />
        <circle cx="50" cy="30" r="19" fill="none" stroke={primaryColor} strokeWidth="0.8" />
        <circle cx="50" cy="30" r="12" fill="none" stroke={primaryColor} strokeWidth="0.4" strokeDasharray="2 1.5" />
      </svg>

      {/* Inner vignette rim */}
      <div
        className="absolute inset-0 pointer-events-none"
        style={{
          boxShadow: "inset 0 2px 10px rgba(24, 22, 18, 0.22), inset 0 0 0 1px rgba(201, 158, 58, 0.28)",
        }}
      />

      {webPath && !imgError ? (
        <img
          src={webPath}
          alt={contentId}
          className="relative z-10 max-w-[86%] max-h-[86%] object-contain drop-shadow-[0_4px_6px_rgba(24,22,18,0.28)] transition-transform duration-200 group-hover:scale-105"
          onError={() => setImgError(true)}
          draggable={false}
        />
      ) : (
        <span
          className="relative z-10 font-display text-2xl font-bold tracking-widest opacity-85 drop-shadow-xs"
          style={{ color: primaryColor }}
        >
          {symbol}
        </span>
      )}
    </div>
  );
};

export const CircuitTrace: React.FC<{
  mirrored?: boolean;
  className?: string;
  tracks?: number;
}> = ({ mirrored = false, className = "", tracks = 3 }) => {
  return (
    <svg
      className={`pointer-events-none opacity-40 ${className}`}
      style={{ transform: mirrored ? "scaleX(-1)" : undefined }}
      viewBox="0 0 480 78"
      fill="none"
    >
      {Array.from({ length: tracks }).map((_, idx) => {
        const y = 16 + idx * 18;
        const turnX = 140 + idx * 45;
        return (
          <g key={idx} stroke="#8d8270" strokeWidth="1.25">
            <path d={`M 0 ${y} H ${turnX} L ${turnX + 18} ${y + 12} H 440`} />
            <circle cx={turnX} cy={y} r="2.5" fill="#b83e25" />
            <circle cx="440" cy={y + 12} r="3" fill="#20201c" />
          </g>
        );
      })}
    </svg>
  );
};
