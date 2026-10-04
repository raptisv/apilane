import {
  ArcElement,
  BarController,
  BarElement,
  CategoryScale,
  Chart,
  Filler,
  Legend,
  LinearScale,
  LineController,
  LineElement,
  PieController,
  PointElement,
  RadarController,
  RadialLinearScale,
  Tooltip,
} from 'chart.js'
import type { ChartConfiguration } from 'chart.js'
import type { ReportChartConfig } from './reportData'

/**
 * Chart.js set up for the report panels: only the chart kinds a report can be (pie, line, bar,
 * radar) and the colours of this theme. Only ReportChart.vue loads this module, through a dynamic
 * import, so Chart.js is a chunk of its own that no other screen downloads. What a chart shows is
 * worked out in lib/reportData.ts (chartConfig).
 */
Chart.register(
  PieController,
  LineController,
  BarController,
  RadarController,
  ArcElement,
  LineElement,
  BarElement,
  PointElement,
  CategoryScale,
  LinearScale,
  RadialLinearScale,
  Legend,
  Tooltip,
  Filler,
)

/** The value of a design token of style.css ('--border'). A canvas cannot use the Tailwind classes. */
function token(name: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim()
}

// Dark is the only theme, so the texts, lines and tooltips of every chart take the tokens once.
Chart.defaults.color = token('--muted-foreground')
Chart.defaults.borderColor = token('--border')
Chart.defaults.font.family = getComputedStyle(document.body).fontFamily
Chart.defaults.plugins.tooltip.backgroundColor = token('--popover')
Chart.defaults.plugins.tooltip.titleColor = token('--popover-foreground')
Chart.defaults.plugins.tooltip.bodyColor = token('--popover-foreground')
Chart.defaults.plugins.tooltip.borderColor = token('--border')
Chart.defaults.plugins.tooltip.borderWidth = 1

/** The colours of the series, in order: the --chart-1 to --chart-10 tokens. */
export function reportPalette(): string[] {
  return Array.from({ length: 10 }, (_, index) => token(`--chart-${index + 1}`)).filter((color) => color !== '')
}

export interface ReportChartHandle {
  /** Frees the chart. Call it before the canvas is reused or removed. */
  destroy: () => void
}

/** Draws a chart on `canvas`. It follows the size of the canvas' parent, which must hold nothing else. */
export function createReportChart(canvas: HTMLCanvasElement, config: ReportChartConfig): ReportChartHandle {
  // reportData keeps its own plain shape, so that it and its tests do not depend on Chart.js.
  const chart = new Chart(canvas, config as unknown as ChartConfiguration)

  return { destroy: () => chart.destroy() }
}
