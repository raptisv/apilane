import { autocompletion, closeBrackets, closeBracketsKeymap, completionKeymap } from '@codemirror/autocomplete'
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands'
import { MSSQL, MySQL, PostgreSQL, SQLite, StandardSQL, sql } from '@codemirror/lang-sql'
import type { SQLDialect } from '@codemirror/lang-sql'
import { bracketMatching, indentOnInput, syntaxHighlighting } from '@codemirror/language'
import { Compartment, EditorState } from '@codemirror/state'
import {
  drawSelection,
  EditorView,
  highlightActiveLine,
  highlightActiveLineGutter,
  keymap,
  lineNumbers,
  placeholder,
  tooltips,
} from '@codemirror/view'

/**
 * CodeMirror 6 set up as the SQL editor of this UI. Only SqlEditor.vue uses it, through a dynamic
 * import, so CodeMirror is downloaded the first time an editor is shown and no other screen pays
 * for it. Use SqlEditor.vue, not this module.
 */

/** Entity name to its property names: what the editor offers while typing. */
export type SqlSchema = Record<string, string[]>

export interface SqlEditorOptions {
  text: string
  schema: SqlSchema
  /** The DatabaseType of the application, for the keywords of its SQL. */
  databaseType?: string
  placeholder?: string
  /** Set on the editable element: aria-label, aria-invalid, aria-describedby. */
  attributes: Record<string, string>
  onChange: (text: string) => void
}

export interface SqlEditorHandle {
  setText: (text: string) => void
  setSchema: (schema: SqlSchema) => void
  setAttributes: (attributes: Record<string, string>) => void
  destroy: () => void
}

const dialects: Record<string, SQLDialect> = { SQLServer: MSSQL, MySQL, SQLLite: SQLite, PostgreSQL }

export function createSqlEditor(parent: HTMLElement, options: SqlEditorOptions): SqlEditorHandle {
  const language = new Compartment()
  const attributes = new Compartment()
  const dialect = dialects[options.databaseType ?? ''] ?? StandardSQL

  const languageFor = (schema: SqlSchema) => sql({ dialect, schema })

  const view = new EditorView({
    parent,
    state: EditorState.create({
      doc: options.text,
      extensions: [
        lineNumbers(),
        highlightActiveLineGutter(),
        highlightActiveLine(),
        history(),
        drawSelection(),
        indentOnInput(),
        bracketMatching(),
        closeBrackets(),
        autocompletion(),
        // Tooltips go in <body> so the frame's overflow-hidden (SqlEditor.vue) does not cut off the
        // completion list; iOS always positions them 'absolute'.
        tooltips({ parent: document.body }),
        // Tab is left to the browser, so the keyboard can leave the editor. Ctrl-Space opens the completion list.
        keymap.of([...closeBracketsKeymap, ...defaultKeymap, ...historyKeymap, ...completionKeymap]),
        EditorView.lineWrapping,
        placeholder(options.placeholder ?? ''),
        language.of(languageFor(options.schema)),
        attributes.of(EditorView.contentAttributes.of(options.attributes)),
        syntaxHighlighting(highlighter),
        theme,
        EditorView.updateListener.of((update) => {
          if (update.docChanged) {
            options.onChange(update.state.doc.toString())
          }
        }),
      ],
    }),
  })

  return {
    setText(text) {
      if (text !== view.state.doc.toString()) {
        view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: text } })
      }
    },
    setSchema(schema) {
      view.dispatch({ effects: language.reconfigure(languageFor(schema)) })
    },
    setAttributes(next) {
      view.dispatch({ effects: attributes.reconfigure(EditorView.contentAttributes.of(next)) })
    },
    destroy() {
      view.destroy()
    },
  }
}

// The colours of the SQL, from the design tokens. CodeMirror names the kind of each piece of text
// with highlighting tags; a tag's `set` is the tag and the more general ones it falls back to
// ('lineComment', then 'comment'), and its name is what String() gives.
type Highlighter = Parameters<typeof syntaxHighlighting>[0]
type Tag = Parameters<Highlighter['style']>[0][number]

const tagClasses: Record<string, string> = {
  keyword: 'cm-sql-keyword',
  typeName: 'cm-sql-type',
  'standard(name)': 'cm-sql-builtin',
  string: 'cm-sql-string',
  number: 'cm-sql-literal',
  bool: 'cm-sql-literal',
  null: 'cm-sql-literal',
  comment: 'cm-sql-comment',
  'special(name)': 'cm-sql-variable',
}

const highlighter: Highlighter = {
  style(tags: readonly Tag[]) {
    for (const tag of tags) {
      for (const candidate of tag.set) {
        const found = tagClasses[String(candidate)]

        if (found) {
          return found
        }
      }
    }

    return null
  },
}

const mono = 'ui-monospace, SFMono-Regular, Menlo, Consolas, "Liberation Mono", monospace'

const theme = EditorView.theme(
  {
    '&': { color: 'var(--foreground)', backgroundColor: 'transparent', fontSize: '0.8125rem' },
    // The frame around the editor (SqlEditor.vue) shows the focus.
    '&.cm-focused': { outline: 'none' },
    '.cm-scroller': { fontFamily: mono, lineHeight: '1.6' },
    '.cm-content': { caretColor: 'var(--foreground)', padding: '0.5rem 0' },
    // At least as tall as the placeholder SqlEditor.vue shows while CodeMirror loads (min-h-48); it grows with the SQL.
    '.cm-content, .cm-gutter': { minHeight: '12rem' },
    '.cm-cursor, .cm-dropCursor': { borderLeftColor: 'var(--foreground)' },
    '&.cm-focused > .cm-scroller > .cm-selectionLayer .cm-selectionBackground, .cm-selectionBackground, .cm-content ::selection': {
      backgroundColor: 'color-mix(in oklab, var(--primary) 40%, transparent)',
    },
    '.cm-gutters': {
      backgroundColor: 'var(--card)',
      color: 'var(--muted-foreground)',
      borderRight: '1px solid var(--border)',
    },
    '.cm-activeLine': { backgroundColor: 'color-mix(in oklab, var(--muted) 45%, transparent)' },
    '.cm-activeLineGutter': { backgroundColor: 'var(--muted)', color: 'var(--foreground)' },
    '.cm-placeholder': { color: 'var(--muted-foreground)' },
    '&.cm-focused .cm-matchingBracket': { backgroundColor: 'color-mix(in oklab, var(--success) 25%, transparent)' },
    '&.cm-focused .cm-nonmatchingBracket': { backgroundColor: 'color-mix(in oklab, var(--destructive) 30%, transparent)' },
    '.cm-tooltip': {
      backgroundColor: 'var(--popover)',
      color: 'var(--popover-foreground)',
      border: '1px solid var(--border)',
      borderRadius: 'calc(var(--radius) - 2px)',
      overflow: 'hidden',
    },
    '.cm-tooltip.cm-tooltip-autocomplete > ul': { fontFamily: mono, maxHeight: '14rem' },
    '.cm-tooltip.cm-tooltip-autocomplete > ul > li': { padding: '0.125rem 0.5rem' },
    '.cm-tooltip.cm-tooltip-autocomplete > ul > li[aria-selected]': {
      backgroundColor: 'var(--accent)',
      color: 'var(--accent-foreground)',
    },
    '.cm-completionMatchedText': { color: 'var(--link)', textDecoration: 'none', fontWeight: '600' },
    '.cm-completionDetail': { color: 'var(--muted-foreground)' },
    '.cm-sql-keyword': { color: 'var(--link)' },
    '.cm-sql-type, .cm-sql-builtin': { color: 'var(--warning)' },
    '.cm-sql-string': { color: 'var(--success)' },
    '.cm-sql-literal': { color: 'var(--destructive)' },
    '.cm-sql-comment': { color: 'var(--muted-foreground)', fontStyle: 'italic' },
    '.cm-sql-variable': { color: 'var(--warning)' },
  },
  { dark: true },
)
