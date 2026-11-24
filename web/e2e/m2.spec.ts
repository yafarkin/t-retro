import { expect, Page, test } from '@playwright/test';
import { closeContextGracefully } from './helpers';

async function login(page: Page, baseURL: string, subject: string, displayName: string): Promise<void> {
  await page.goto(`${baseURL}/login`);
  await page.getByLabel('Идентификатор сотрудника').fill(subject);
  await page.getByLabel('Отображаемое имя').fill(displayName);
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Мои встречи' })).toBeVisible();
}

test('M2 presence, live state, role transfer and access revocation synchronize clients', async ({ browser, baseURL }) => {
  const origin = baseURL!;
  const firstContext = await browser.newContext();
  const secondContext = await browser.newContext();
  const first = await firstContext.newPage();
  const second = await secondContext.newPage();

  await login(first, origin, 'm2-first', 'Первый M2');
  await login(second, origin, 'm2-second', 'Второй M2');
  await first.goto(`${origin}/meetings/new`);
  await first.getByRole('button', { name: 'Выбрать шаблон' }).click();
  await first.getByLabel('Название встречи').fill('Ретро M2 realtime');
  await first.getByRole('button', { name: 'Создать встречу' }).click();
  await expect(first.getByRole('heading', { name: 'Ретро M2 realtime', exact: true })).toBeVisible();
  await first.getByRole('button', { name: 'Показать приглашение' }).click();
  const invite = await first.getByLabel('Ссылка приглашения').inputValue();

  await second.goto(invite);
  await second.getByRole('button', { name: 'Вступить во встречу' }).click();
  await expect(second.getByRole('heading', { name: 'Ретро M2 realtime', exact: true })).toBeVisible();
  await expect(first.locator('li').filter({ hasText: 'Второй M2' })).toContainText('онлайн');

  const secondTab = await firstContext.newPage();
  await secondTab.goto(first.url());
  await expect(secondTab.getByRole('heading', { name: 'Ретро M2 realtime', exact: true })).toBeVisible();
  await secondTab.close();
  await expect(first.locator('li').filter({ hasText: 'Первый M2' })).toContainText('онлайн');

  await first.getByLabel('Название встречи').fill('Ретро M2 после обновления');
  await first.getByRole('button', { name: 'Сохранить настройки' }).click();
  await expect(second.getByRole('heading', { name: 'Ретро M2 после обновления', exact: true })).toBeVisible();

  await first.getByLabel('Участник').selectOption({ label: 'Второй M2' });
  first.once('dialog', (dialog) => dialog.accept());
  await first.getByRole('button', { name: 'Передать роль' }).click();
  await expect(second.getByRole('button', { name: 'Показать приглашение' })).toBeVisible();
  await expect(first.getByText('Настройки изменяет только фасилитатор.')).toBeVisible();

  await second.getByLabel('Участник').selectOption({ label: 'Первый M2' });
  second.once('dialog', (dialog) => dialog.accept());
  await second.getByRole('button', { name: 'Исключить', exact: true }).click();
  await expect(first.getByRole('heading', { name: 'Доступ к встрече закрыт' })).toBeVisible();
  await expect(second.locator('li').filter({ hasText: 'Первый M2' })).toContainText('исключён');
  await expect(second.locator('li').filter({ hasText: 'Первый M2' })).toContainText('офлайн');

  await Promise.all([firstContext, secondContext].map(closeContextGracefully));
});
