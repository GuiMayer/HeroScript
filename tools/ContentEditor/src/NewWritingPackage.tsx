import { useState } from 'react'
import { Plus } from 'lucide-react'
import { Modal } from './Modal'
import type { RuleCopy } from './rule-copy'

export function NewWritingPackage({ copy, closeLabel, cancelLabel, close, ready, create }: {
  copy: RuleCopy; closeLabel: string; cancelLabel: string; close: () => void; ready: boolean
  create: (directory: string, packageId: string, version: string) => boolean
}) {
  const [invalid, setInvalid] = useState(false)
  return <Modal title={copy.newWritingPackage} onClose={close} closeLabel={closeLabel}>
    <p className="muted">{copy.writingPackageHint}</p>
    <form onSubmit={(event) => {
      event.preventDefault()
      const data = new FormData(event.currentTarget)
      if (!create(String(data.get('directory')).trim(), String(data.get('packageId')).trim(),
        String(data.get('version')).trim())) setInvalid(true)
    }}>
      <label htmlFor="writing-package-directory">{copy.packageDirectory}</label>
      <input id="writing-package-directory" name="directory" required autoFocus maxLength={128} />
      <label htmlFor="writing-package-id">{copy.packageId}</label>
      <input id="writing-package-id" name="packageId" required maxLength={128} />
      <label htmlFor="writing-package-version">{copy.packageVersion}</label>
      <input id="writing-package-version" name="version" required defaultValue="1.0.0" />
      {invalid && <p className="error" role="alert">{copy.invalidId}</p>}
      <div className="dialog-actions">
        <button className="secondary" type="button" onClick={close}>{cancelLabel}</button>
        <button className="primary" type="submit" disabled={!ready}><Plus size={15} />{copy.createAndLink}</button>
      </div>
    </form>
  </Modal>
}
