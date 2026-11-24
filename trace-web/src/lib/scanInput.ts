import type { InputHTMLAttributes } from 'react';

export const scanInputProps: Pick<InputHTMLAttributes<HTMLInputElement>, 'onFocus' | 'onPaste' | 'onKeyUp'> = {
  onFocus: (event) => event.currentTarget.select(),
  onPaste: (event) => event.currentTarget.select(),
  onKeyUp: (event) => {
    if (event.key === 'Enter') event.currentTarget.select();
  },
};
