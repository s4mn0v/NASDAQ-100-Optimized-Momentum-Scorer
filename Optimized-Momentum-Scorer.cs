#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public class TheIndicatorOptimized : Indicator
	{
		private HMA hmaL, hmaS;
		private ATR atr;
		private Bollinger bb;
		private EMA ema200, macd_e1, macd_e2, macd_signal;
		
		private Series<double> f28, f30, f38, f40, f48, f50;
		private Series<double> f58, f60, f68, f70, f78, f80;
		
		private double dHigh, dLow;
		private int trendAge = 0;
		private bool entryAllowed = true;
		private bool lastHmaUp = false;

		private double lastSwingHigh = double.MinValue;
		private double lastSwingLow  = double.MaxValue;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @" NASDAQ 100 momentum scorer in Pine Script v6. Uses a multi-factor scoring engine (HMA, RSX, MACD) with spread and volatility filters. Optimized for NQ trend entries with trend age tracking and real-time dashboard.";
				Name										= "Optimized-Momentum-Scorer";
				Calculate									= Calculate.OnBarClose;
				IsOverlay									= true;
				DisplayInDataBox							= true;

				HmaL_len = 21; HmaS_len = 8; Rsx_len = 14; Atr_len = 100;
				BbLen = 20; MinBBW = 0.5; MaxAge = 15; Macd_ma = 34;
				Macd_sig = 9; MaxSpread = 2.5; 
				
				AtrMultiplier = 1.5;
				UseAnchor = true;

				UseSwingFilter   = true;
				SwingStrength    = 3;
				AllowReentry     = true;
				MaxExtensionATR  = 3.0;
				AtrTPMultiplier  = 3.0;
				
				AddPlot(new Stroke(Brushes.Lime, DashStyleHelper.Solid, 2), PlotStyle.Line, "HmaSPlot");
				AddPlot(new Stroke(Brushes.Gray, DashStyleHelper.Dash, 1), PlotStyle.Line, "BBUpper");
				AddPlot(new Stroke(Brushes.Gray, DashStyleHelper.Dash, 1), PlotStyle.Line, "BBLower");
				AddPlot(new Stroke(Brushes.Goldenrod, DashStyleHelper.Dot, 1), PlotStyle.Line, "DayHigh");
				AddPlot(new Stroke(Brushes.SteelBlue, DashStyleHelper.Dot, 1), PlotStyle.Line, "DayLow");
			}
			else if (State == State.Configure)
			{
				if (UseAnchor)
					AddDataSeries(BarsPeriod.BarsPeriodType, BarsPeriod.Value * 5);
			}
			else if (State == State.DataLoaded)
			{
				hmaL = HMA(HmaL_len);
				hmaS = HMA(HmaS_len);
				atr = ATR(Atr_len);
				bb = Bollinger(2.0, BbLen);
				ema200 = EMA(200);
				
				macd_e1 = EMA(Macd_ma);
				macd_e2 = EMA(macd_e1, Macd_ma);
				macd_signal = EMA(macd_e1, Macd_sig);

				f28 = new Series<double>(this); f30 = new Series<double>(this);
				f38 = new Series<double>(this); f40 = new Series<double>(this);
				f48 = new Series<double>(this); f50 = new Series<double>(this);
				f58 = new Series<double>(this); f60 = new Series<double>(this);
				f68 = new Series<double>(this); f70 = new Series<double>(this);
				f78 = new Series<double>(this); f80 = new Series<double>(this);
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) return;

			if (CurrentBar < 201 || (UseAnchor && CurrentBars[1] < 201)) return;

			double rsxVal = CalculateRSX();

			if (Bars.IsFirstBarOfSession) { dHigh = High[0]; dLow = Low[0]; }
			else { dHigh = Math.Max(dHigh, High[0]); dLow = Math.Min(dLow, Low[0]); }
			double dailyMid = (dHigh + dLow) / 2;

			bool volatilityOK = (High[0] - Low[0]) > (atr[0] * 0.2); 
			bool marketActive = (bb.Upper[0] - bb.Lower[0]) > (bb.Middle[0] * (MinBBW / 100));
			double currentSpread = (State == State.Realtime) ? (GetCurrentAsk() - GetCurrentBid()) / TickSize : 0;
			bool spreadOK = State != State.Realtime || currentSpread <= MaxSpread;

			bool anchorLong = !UseAnchor || (Close[0] > ema200[0] && Closes[1][0] > EMA(BarsArray[1], 200)[0]);
			bool anchorShort = !UseAnchor || (Close[0] < ema200[0] && Closes[1][0] < EMA(BarsArray[1], 200)[0]);

			// Trend Age
			bool hmaUp = hmaS[0] > hmaL[0];
			if (hmaUp != lastHmaUp) { trendAge = 0; entryAllowed = true; }
			else trendAge++;
			lastHmaUp = hmaUp;

			double prevSwingHigh = lastSwingHigh;
			double prevSwingLow  = lastSwingLow;
			bool newSwingHigh = false, newSwingLow = false;

			if (UseSwingFilter && CurrentBar >= SwingStrength * 2)
			{
				if (IsSwingHigh(SwingStrength))
				{
					lastSwingHigh = High[SwingStrength];
					newSwingHigh = true;
					Draw.Dot(this, "SwH" + (CurrentBar - SwingStrength), true, SwingStrength, High[SwingStrength] + (TickSize * 4), Brushes.OrangeRed);
				}
				if (IsSwingLow(SwingStrength))
				{
					lastSwingLow = Low[SwingStrength];
					newSwingLow = true;
					Draw.Dot(this, "SwL" + (CurrentBar - SwingStrength), true, SwingStrength, Low[SwingStrength] - (TickSize * 4), Brushes.DodgerBlue);
				}
			}

			// Scoring
			double zlema = macd_e1[0] + (macd_e1[0] - macd_e2[0]);
			int score = 0;
			score += hmaUp ? 2 : -2;
			score += (zlema > macd_signal[0]) ? 1 : -1;
			score += (rsxVal > 50) ? 1 : -1;
			score += (hmaS[0] > hmaS[1]) ? 1 : -1;
			score += (Close[0] > dailyMid) ? 1 : -1;

			bool structureLongOK  = !UseSwingFilter || lastSwingLow == double.MaxValue  || Low[0]  >= lastSwingLow;
			bool structureShortOK = !UseSwingFilter || lastSwingHigh == double.MinValue || High[0] <= lastSwingHigh;

			bool extensionOK = true;
			if (MaxExtensionATR > 0)
			{
				extensionOK = hmaUp
					? (Close[0] - dLow)  <= atr[0] * MaxExtensionATR
					: (dHigh - Close[0]) <= atr[0] * MaxExtensionATR;
			}

			if (entryAllowed && marketActive && volatilityOK && spreadOK && trendAge <= MaxAge)
			{
				if (hmaUp && score >= 4 && anchorLong && structureLongOK && extensionOK && Close[0] > High[1])
				{
					Draw.ArrowUp(this, "L" + CurrentBar, true, 0, Low[0] - (atr[0] * AtrMultiplier), Brushes.Lime);

					if (AtrTPMultiplier > 0)
					{
						double tp = Close[0] + atr[0] * AtrTPMultiplier;
						Draw.Text(this, "TPL" + CurrentBar, "TP " + tp.ToString("F2"), 0, tp, Brushes.Lime);
					}
					entryAllowed = false;
				}
				else if (!hmaUp && score <= -4 && anchorShort && structureShortOK && extensionOK && Close[0] < Low[1])
				{
					Draw.ArrowDown(this, "S" + CurrentBar, true, 0, High[0] + (atr[0] * AtrMultiplier), Brushes.Red);

					if (AtrTPMultiplier > 0)
					{
						double tp = Close[0] - atr[0] * AtrTPMultiplier;
						Draw.Text(this, "TPS" + CurrentBar, "TP " + tp.ToString("F2"), 0, tp, Brushes.Red);
					}
					entryAllowed = false;
				}
			}

			if (AllowReentry && UseSwingFilter)
			{
				if (hmaUp && newSwingLow && lastSwingLow > prevSwingLow)
					entryAllowed = true;
				if (!hmaUp && newSwingHigh && lastSwingHigh < prevSwingHigh)
					entryAllowed = true;
			}

			HmaSPlot[0] = hmaS[0];
			BBUpper[0] = bb.Upper[0];
			BBLower[0] = bb.Lower[0];
			DayHigh[0] = dHigh;
			DayLow[0] = dLow;
		}

		private double CalculateRSX()
		{
			if (CurrentBar < 1) return 50;
			double f18 = 3.0 / (Rsx_len + 2.0);
			double f20 = 1.0 - f18;
			double v8 = (Input[0] - Input[1]) * 100;
			f28[0] = f20 * f28[1] + f18 * v8;
			f30[0] = f18 * f28[0] + f20 * f30[1];
			double vC = f28[0] * 1.5 - f30[0] * 0.5;
			f38[0] = f20 * f38[1] + f18 * vC;
			f40[0] = f18 * f38[0] + f20 * f40[1];
			double v10 = f38[0] * 1.5 - f40[0] * 0.5;
			f48[0] = f20 * f48[1] + f18 * v10;
			f50[0] = f18 * f48[0] + f20 * f50[1];
			double v14 = f48[0] * 1.5 - f50[0] * 0.5;
			f58[0] = f20 * f58[1] + f18 * Math.Abs(v8);
			f60[0] = f18 * f58[0] + f20 * f60[1];
			double v18 = f58[0] * 1.5 - f60[0] * 0.5;
			f68[0] = f20 * f68[1] + f18 * v18;
			f70[0] = f18 * f68[0] + f20 * f70[1];
			double v1C = f68[0] * 1.5 - f70[0] * 0.5;
			f78[0] = f20 * f78[1] + f18 * v1C;
			f80[0] = f18 * f78[0] + f20 * f80[1];
			double v20 = f78[0] * 1.5 - f80[0] * 0.5;
			return v20 > 0 ? (v14 / v20 + 1) * 50 : 50;
		}

		private bool IsSwingHigh(int strength)
		{
			double pivotVal = High[strength];
			for (int i = 0; i <= strength * 2; i++)
			{
				if (i == strength) continue;
				if (High[i] >= pivotVal) return false;
			}
			return true;
		}

		private bool IsSwingLow(int strength)
		{
			double pivotVal = Low[strength];
			for (int i = 0; i <= strength * 2; i++)
			{
				if (i == strength) continue;
				if (Low[i] <= pivotVal) return false;
			}
			return true;
		}

		#region Properties
		[Browsable(false), XmlIgnore] public Series<double> HmaSPlot { get { return Values[0]; } }
		[Browsable(false), XmlIgnore] public Series<double> BBUpper { get { return Values[1]; } }
		[Browsable(false), XmlIgnore] public Series<double> BBLower { get { return Values[2]; } }
		[Browsable(false), XmlIgnore] public Series<double> DayHigh { get { return Values[3]; } }
		[Browsable(false), XmlIgnore] public Series<double> DayLow { get { return Values[4]; } }

		[NinjaScriptProperty, Range(1, int.MaxValue), Display(Name="HMA Largo", GroupName="1. Parámetros", Order=0)]
		public int HmaL_len { get; set; }
		[NinjaScriptProperty, Range(1, int.MaxValue), Display(Name="HMA Corto", GroupName="1. Parámetros", Order=1)]
		public int HmaS_len { get; set; }
		[NinjaScriptProperty, Range(1, int.MaxValue), Display(Name="RSX Length", GroupName="1. Parámetros", Order=2)]
		public int Rsx_len { get; set; }
		[NinjaScriptProperty, Range(1, int.MaxValue), Display(Name="ATR Length", GroupName="1. Parámetros", Order=3)]
		public int Atr_len { get; set; }
		[NinjaScriptProperty, Range(1, int.MaxValue), Display(Name="BB Length", GroupName="1. Parámetros", Order=4)]
		public int BbLen { get; set; }
		[NinjaScriptProperty, Range(0, double.MaxValue), Display(Name="Min BB Width %", GroupName="1. Parámetros", Order=5)]
		public double MinBBW { get; set; }
		[NinjaScriptProperty, Range(1, int.MaxValue), Display(Name="Max Age Bars", GroupName="1. Parámetros", Order=6)]
		public int MaxAge { get; set; }
		[NinjaScriptProperty, Range(1, int.MaxValue), Display(Name="MACD MA", GroupName="1. Parámetros", Order=7)]
		public int Macd_ma { get; set; }
		[NinjaScriptProperty, Range(1, int.MaxValue), Display(Name="MACD Signal", GroupName="1. Parámetros", Order=8)]
		public int Macd_sig { get; set; }
		[NinjaScriptProperty, Range(0, double.MaxValue), Display(Name="Max Spread Ticks", GroupName="1. Parámetros", Order=9)]
		public double MaxSpread { get; set; }

		[NinjaScriptProperty, Range(0.1, 10.0), Display(Name="ATR Multiplier Arrow", GroupName="1. Parámetros", Order=10)]
		public double AtrMultiplier { get; set; }
		
		[NinjaScriptProperty, Display(Name="Use Anchor Trend", GroupName="1. Parámetros", Order=11)]
		public bool UseAnchor { get; set; }

		[NinjaScriptProperty, Display(Name="Usar Filtro de Estructura (Swing)", GroupName="2. Estructura Swing", Order=12)]
		public bool UseSwingFilter { get; set; }

		[NinjaScriptProperty, Range(1, 50), Display(Name="Fuerza del Swing (barras)", GroupName="2. Estructura Swing", Order=13)]
		public int SwingStrength { get; set; }

		[NinjaScriptProperty, Display(Name="Permitir Re-entrada en la misma tendencia", GroupName="2. Estructura Swing", Order=14)]
		public bool AllowReentry { get; set; }

		[NinjaScriptProperty, Range(0, double.MaxValue), Display(Name="Max Extension (x ATR desde rango diario)", GroupName="3. Gestión de Riesgo", Order=15)]
		public double MaxExtensionATR { get; set; }

		[NinjaScriptProperty, Range(0, double.MaxValue), Display(Name="Target Referencia (x ATR)", GroupName="3. Gestión de Riesgo", Order=16)]
		public double AtrTPMultiplier { get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private TheIndicatorOptimized[] cacheTheIndicatorOptimized;
		public TheIndicatorOptimized TheIndicatorOptimized(int hmaL_len, int hmaS_len, int rsx_len, int atr_len, int bbLen, double minBBW, int maxAge, int macd_ma, int macd_sig, double maxSpread, double atrMultiplier, bool useAnchor, bool useSwingFilter, int swingStrength, bool allowReentry, double maxExtensionATR, double atrTPMultiplier)
		{
			return TheIndicatorOptimized(Input, hmaL_len, hmaS_len, rsx_len, atr_len, bbLen, minBBW, maxAge, macd_ma, macd_sig, maxSpread, atrMultiplier, useAnchor, useSwingFilter, swingStrength, allowReentry, maxExtensionATR, atrTPMultiplier);
		}

		public TheIndicatorOptimized TheIndicatorOptimized(ISeries<double> input, int hmaL_len, int hmaS_len, int rsx_len, int atr_len, int bbLen, double minBBW, int maxAge, int macd_ma, int macd_sig, double maxSpread, double atrMultiplier, bool useAnchor, bool useSwingFilter, int swingStrength, bool allowReentry, double maxExtensionATR, double atrTPMultiplier)
		{
			if (cacheTheIndicatorOptimized != null)
				for (int idx = 0; idx < cacheTheIndicatorOptimized.Length; idx++)
					if (cacheTheIndicatorOptimized[idx] != null && cacheTheIndicatorOptimized[idx].HmaL_len == hmaL_len && cacheTheIndicatorOptimized[idx].HmaS_len == hmaS_len && cacheTheIndicatorOptimized[idx].Rsx_len == rsx_len && cacheTheIndicatorOptimized[idx].Atr_len == atr_len && cacheTheIndicatorOptimized[idx].BbLen == bbLen && cacheTheIndicatorOptimized[idx].MinBBW == minBBW && cacheTheIndicatorOptimized[idx].MaxAge == maxAge && cacheTheIndicatorOptimized[idx].Macd_ma == macd_ma && cacheTheIndicatorOptimized[idx].Macd_sig == macd_sig && cacheTheIndicatorOptimized[idx].MaxSpread == maxSpread && cacheTheIndicatorOptimized[idx].AtrMultiplier == atrMultiplier && cacheTheIndicatorOptimized[idx].UseAnchor == useAnchor && cacheTheIndicatorOptimized[idx].UseSwingFilter == useSwingFilter && cacheTheIndicatorOptimized[idx].SwingStrength == swingStrength && cacheTheIndicatorOptimized[idx].AllowReentry == allowReentry && cacheTheIndicatorOptimized[idx].MaxExtensionATR == maxExtensionATR && cacheTheIndicatorOptimized[idx].AtrTPMultiplier == atrTPMultiplier && cacheTheIndicatorOptimized[idx].EqualsInput(input))
						return cacheTheIndicatorOptimized[idx];
			return CacheIndicator<TheIndicatorOptimized>(new TheIndicatorOptimized(){ HmaL_len = hmaL_len, HmaS_len = hmaS_len, Rsx_len = rsx_len, Atr_len = atr_len, BbLen = bbLen, MinBBW = minBBW, MaxAge = maxAge, Macd_ma = macd_ma, Macd_sig = macd_sig, MaxSpread = maxSpread, AtrMultiplier = atrMultiplier, UseAnchor = useAnchor, UseSwingFilter = useSwingFilter, SwingStrength = swingStrength, AllowReentry = allowReentry, MaxExtensionATR = maxExtensionATR, AtrTPMultiplier = atrTPMultiplier }, input, ref cacheTheIndicatorOptimized);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.TheIndicatorOptimized TheIndicatorOptimized(int hmaL_len, int hmaS_len, int rsx_len, int atr_len, int bbLen, double minBBW, int maxAge, int macd_ma, int macd_sig, double maxSpread, double atrMultiplier, bool useAnchor, bool useSwingFilter, int swingStrength, bool allowReentry, double maxExtensionATR, double atrTPMultiplier)
		{
			return indicator.TheIndicatorOptimized(Input, hmaL_len, hmaS_len, rsx_len, atr_len, bbLen, minBBW, maxAge, macd_ma, macd_sig, maxSpread, atrMultiplier, useAnchor, useSwingFilter, swingStrength, allowReentry, maxExtensionATR, atrTPMultiplier);
		}

		public Indicators.TheIndicatorOptimized TheIndicatorOptimized(ISeries<double> input , int hmaL_len, int hmaS_len, int rsx_len, int atr_len, int bbLen, double minBBW, int maxAge, int macd_ma, int macd_sig, double maxSpread, double atrMultiplier, bool useAnchor, bool useSwingFilter, int swingStrength, bool allowReentry, double maxExtensionATR, double atrTPMultiplier)
		{
			return indicator.TheIndicatorOptimized(input, hmaL_len, hmaS_len, rsx_len, atr_len, bbLen, minBBW, maxAge, macd_ma, macd_sig, maxSpread, atrMultiplier, useAnchor, useSwingFilter, swingStrength, allowReentry, maxExtensionATR, atrTPMultiplier);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.TheIndicatorOptimized TheIndicatorOptimized(int hmaL_len, int hmaS_len, int rsx_len, int atr_len, int bbLen, double minBBW, int maxAge, int macd_ma, int macd_sig, double maxSpread, double atrMultiplier, bool useAnchor, bool useSwingFilter, int swingStrength, bool allowReentry, double maxExtensionATR, double atrTPMultiplier)
		{
			return indicator.TheIndicatorOptimized(Input, hmaL_len, hmaS_len, rsx_len, atr_len, bbLen, minBBW, maxAge, macd_ma, macd_sig, maxSpread, atrMultiplier, useAnchor, useSwingFilter, swingStrength, allowReentry, maxExtensionATR, atrTPMultiplier);
		}

		public Indicators.TheIndicatorOptimized TheIndicatorOptimized(ISeries<double> input , int hmaL_len, int hmaS_len, int rsx_len, int atr_len, int bbLen, double minBBW, int maxAge, int macd_ma, int macd_sig, double maxSpread, double atrMultiplier, bool useAnchor, bool useSwingFilter, int swingStrength, bool allowReentry, double maxExtensionATR, double atrTPMultiplier)
		{
			return indicator.TheIndicatorOptimized(input, hmaL_len, hmaS_len, rsx_len, atr_len, bbLen, minBBW, maxAge, macd_ma, macd_sig, maxSpread, atrMultiplier, useAnchor, useSwingFilter, swingStrength, allowReentry, maxExtensionATR, atrTPMultiplier);
		}
	}
}

#endregion
