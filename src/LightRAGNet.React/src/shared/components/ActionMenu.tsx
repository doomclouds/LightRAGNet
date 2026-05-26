import { MoreVertical } from 'lucide-react';
import type { ReactNode } from 'react';
import { useEffect, useRef, useState } from 'react';

type ActionMenuItem = {
  label: string;
  icon?: ReactNode;
  tone?: 'neutral' | 'danger';
  disabled?: boolean;
  onSelect: () => void;
};

type ActionMenuProps = {
  label: string;
  items: ActionMenuItem[];
  className?: string;
};

export function ActionMenu({ label, items, className }: ActionMenuProps) {
  const [isOpen, setIsOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!isOpen) {
      return;
    }

    function handlePointerDown(event: MouseEvent) {
      if (!menuRef.current?.contains(event.target as Node)) {
        setIsOpen(false);
      }
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setIsOpen(false);
      }
    }

    document.addEventListener('mousedown', handlePointerDown);
    document.addEventListener('keydown', handleKeyDown);

    return () => {
      document.removeEventListener('mousedown', handlePointerDown);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [isOpen]);

  return (
    <div className={['lrn-action-menu', className].filter(Boolean).join(' ')} ref={menuRef}>
      <button
        type="button"
        className="lrn-action-menu__trigger"
        aria-label={label}
        aria-haspopup="menu"
        aria-expanded={isOpen}
        onClick={() => setIsOpen((current) => !current)}
      >
        <MoreVertical size={16} aria-hidden="true" />
      </button>
      {isOpen ? (
        <div className="lrn-action-menu__content" role="menu">
          {items.map((item) => (
            <button
              key={item.label}
              type="button"
              role="menuitem"
              className={['lrn-action-menu__item', item.tone === 'danger' ? 'lrn-action-menu__item--danger' : undefined].filter(Boolean).join(' ')}
              disabled={item.disabled}
              onClick={() => {
                item.onSelect();
                setIsOpen(false);
              }}
            >
              {item.icon ? <span className="lrn-action-menu__item-icon" aria-hidden="true">{item.icon}</span> : null}
              <span>{item.label}</span>
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}
