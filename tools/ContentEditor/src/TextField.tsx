import { useEffect, useState } from 'react'

export function TextField({ label, value, disabled, multiline = false, onCommit, onTyping }: {
  label: string; value: string; disabled: boolean; multiline?: boolean; onCommit: (value: string) => void
  onTyping: (dirty: boolean) => void
}) {
  const [text, setText] = useState(value)
  useEffect(() => setText(value), [value])
  const props = {
    value: text, disabled,
    onBlur: () => { onTyping(false); if (text !== value) onCommit(text) },
    onChange: (event: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
      setText(event.target.value)
      onTyping(event.target.value !== value)
    },
  }
  return <label className="field"><span>{label}</span>{multiline
    ? <textarea {...props} rows={3} />
    : <input {...props} onKeyDown={(event) => { if (event.key === 'Enter') event.currentTarget.blur() }} />}</label>
}
