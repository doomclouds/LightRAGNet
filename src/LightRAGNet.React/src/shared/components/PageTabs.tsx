export type PageTabItem = {
  id: string;
  label: string;
  href: string;
  badge?: string | number;
};

type PageTabsProps = {
  tabs: PageTabItem[];
  currentId: string;
  label?: string;
};

export function PageTabs({ tabs, currentId, label = 'Page sections' }: PageTabsProps) {
  return (
    <nav className="lrn-page-tabs" aria-label={label}>
      {tabs.map((tab) => (
        <a
          key={tab.id}
          className="lrn-page-tabs__item"
          href={tab.href}
          aria-current={tab.id === currentId ? 'page' : undefined}
        >
          <span>{tab.label}</span>
          {tab.badge !== undefined ? <span className="lrn-page-tabs__badge" aria-hidden="true">{tab.badge}</span> : null}
        </a>
      ))}
    </nav>
  );
}
