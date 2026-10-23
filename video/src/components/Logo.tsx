import React from "react";

/** A small, original mark: rounded badge + isometric cube outline. */
export const Logo: React.FC<{ size?: number; withWordmark?: boolean }> = ({
  size = 56,
  withWordmark = false,
}) => {
  return (
    <div style={{ display: "flex", alignItems: "center", gap: size * 0.32 }}>
      <div
        style={{
          width: size,
          height: size,
          borderRadius: size * 0.28,
          background:
            "linear-gradient(145deg, rgba(110,231,168,0.95), rgba(34,201,138,0.72))",
          boxShadow:
            "0 12px 40px rgba(74,222,128,0.34), inset 0 1px 0 rgba(255,255,255,0.55)",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
        }}
      >
        <svg width={size * 0.56} height={size * 0.56} viewBox="0 0 24 24" fill="none">
          <path
            d="M12 2.6 21 7.4v9.2L12 21.4 3 16.6V7.4L12 2.6Z"
            stroke="#05110c"
            strokeWidth="1.7"
            strokeLinejoin="round"
          />
          <path
            d="M3.4 7.6 12 12.3l8.6-4.7M12 12.3v8.7"
            stroke="#05110c"
            strokeWidth="1.7"
            strokeLinejoin="round"
            strokeLinecap="round"
          />
        </svg>
      </div>
      {withWordmark ? (
        <div style={{ display: "flex", flexDirection: "column", lineHeight: 1 }}>
          <span
            style={{
              fontFamily: '"Segoe UI", "Microsoft YaHei UI", sans-serif',
              fontWeight: 800,
              fontSize: size * 0.5,
              letterSpacing: "-0.02em",
              color: "#f2f7f8",
            }}
          >
            QS Launcher
          </span>
          <span
            style={{
              fontFamily: '"Cascadia Code", "Consolas", monospace',
              fontSize: size * 0.19,
              letterSpacing: "0.18em",
              color: "rgba(167,184,189,0.8)",
              marginTop: size * 0.13,
            }}
          >
            MINECRAFT JAVA
          </span>
        </div>
      ) : null}
    </div>
  );
};
