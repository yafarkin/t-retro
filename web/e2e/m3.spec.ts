import { expect, Page, test } from '@playwright/test';
import { closeContextGracefully } from './helpers';

async function login(page: Page, baseURL: string, subject: string, displayName: string): Promise<void> {
  await page.goto(`${baseURL}/login`);
  await page.getByLabel('Идентификатор сотрудника').fill(subject);
  await page.getByLabel('Отображаемое имя').fill(displayName);
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Мои встречи' })).toBeVisible();
}

test('M3 private cards, readiness, late join, exclusion and phase advance synchronize clients', async ({ browser, baseURL }) => {
  const origin = baseURL!;
  const firstContext = await browser.newContext();
  const secondContext = await browser.newContext();
  const thirdContext = await browser.newContext();
  const first = await firstContext.newPage();
  const second = await secondContext.newPage();
  const third = await thirdContext.newPage();

  await login(first, origin, 'm3-first', 'Первый M3');
  await login(second, origin, 'm3-second', 'Второй M3');
  await login(third, origin, 'm3-third', 'Третий M3');
  await first.goto(`${origin}/meetings/new`);
  await first.getByRole('button', { name: 'Выбрать шаблон' }).click();
  await first.getByLabel('Название встречи').fill('Ретро M3 приватность');
  await first.getByRole('button', { name: 'Настройки ретро' }).click();
  await first.getByLabel('Скрывать авторов карточек').uncheck();
  await first.getByRole('button', { name: 'Сохранить настройки' }).click();
  await first.getByRole('button', { name: 'Создать ретро', exact: true }).click();
  await expect(first.getByRole('heading', { name: 'Ретро M3 приватность', exact: true })).toBeVisible();
  await first.getByRole('button', { name: 'Показать приглашение' }).click();
  const invite = await first.getByLabel('Ссылка приглашения').inputValue();
  await first.getByRole('button', { name: 'Закрыть приглашение' }).click();
  await second.goto(invite);
  await second.getByRole('button', { name: 'Вступить во встречу' }).click();
  await expect(second.getByRole('heading', { name: 'Ретро M3 приватность', exact: true })).toBeVisible();

  await expect(second.getByText('Сбор карточек', { exact: true })).toBeVisible();
  const firstColumn = first.getByRole('region', { name: 'Начать', exact: true });
  await firstColumn.getByRole('button', { name: 'Добавить карточку' }).click();
  await firstColumn.getByLabel('Новая карточка').fill('Карточка ведущего');
  await firstColumn.getByRole('button', { name: 'Добавить карточку' }).click();
  await expect(firstColumn.getByText('Карточка ведущего', { exact: true })).toBeVisible();

  const secondColumn = second.getByRole('region', { name: 'Начать', exact: true });
  await secondColumn.getByRole('button', { name: 'Добавить карточку' }).click();
  await secondColumn.getByLabel('Авторство карточки').selectOption({ label: 'Анонимно' });
  await secondColumn.getByLabel('Новая карточка').fill('Секрет участника');
  await secondColumn.getByRole('button', { name: 'Добавить карточку' }).click();
  const secondOwnCard = secondColumn.locator('.card').filter({ hasText: 'Секрет участника' });
  await expect(secondOwnCard).toContainText('Моя карточка');
  await expect(secondOwnCard).toContainText('Анонимно');
  await secondColumn.getByRole('button', { name: 'Добавить карточку' }).click();
  await expect(secondColumn.getByLabel('Авторство карточки').locator('option:checked')).toHaveText('С именем');
  await secondColumn.getByRole('button', { name: 'Отмена', exact: true }).click();
  await expect(firstColumn.getByText('Карточка скрыта до группировки', { exact: true })).toHaveCount(1);
  await expect(first.getByText('Секрет участника', { exact: true })).toHaveCount(0);
  await expect(secondColumn.locator('.hidden-card')).not.toContainText('Первый M3');

  await secondOwnCard.getByRole('button', { name: 'Изменить', exact: true }).click();
  await secondColumn.getByLabel('Текст редактируемой карточки').fill('Обновлённый секрет');
  await secondColumn.getByRole('button', { name: 'Сохранить карточку' }).click();
  await expect(secondColumn.getByText('Обновлённый секрет', { exact: true })).toBeVisible();
  await expect(first.getByText('Обновлённый секрет', { exact: true })).toHaveCount(0);
  await expect(firstColumn.getByText('Карточка скрыта до группировки', { exact: true })).toHaveCount(1);

  await first.getByLabel('Я готов').check();
  await expect(second.getByRole('heading', { name: 'Готово 1/2' })).toBeVisible();
  await second.getByLabel('Я готов').check();
  await expect(first.getByRole('heading', { name: 'Все готовы (2/2)' })).toBeVisible();
  await expect(first.getByText('Сбор карточек', { exact: true })).toBeVisible();

  await third.goto(invite);
  await third.getByRole('button', { name: 'Вступить во встречу' }).click();
  await expect(first.getByRole('heading', { name: 'Готово 2/3' })).toBeVisible();
  await expect(third.getByLabel('Я готов')).not.toBeChecked();
  await third.getByLabel('Я готов').check();
  await expect(first.getByRole('heading', { name: 'Все готовы (3/3)' })).toBeVisible();
  await first.getByRole('combobox', { name: 'Участник', exact: true }).selectOption({ label: 'Третий M3' });
  first.once('dialog', (dialog) => dialog.accept());
  await first.getByRole('button', { name: 'Исключить', exact: true }).click();
  await expect(third.getByRole('heading', { name: 'Доступ к встрече закрыт' })).toBeVisible();
  await expect(first.getByRole('heading', { name: 'Все готовы (2/2)' })).toBeVisible();

  await first.getByRole('button', { name: 'Перейти к группировке' }).click();
  await expect(second.getByText('Группировка', { exact: true })).toBeVisible();
  await expect(secondColumn.getByText('Обновлённый секрет', { exact: true })).toBeVisible();
  await expect(firstColumn.getByText('Обновлённый секрет', { exact: true })).toBeVisible();
  await expect(firstColumn.getByText('Карточка скрыта до группировки', { exact: true })).toHaveCount(0);
  await expect(second.getByLabel('Новая карточка')).toHaveCount(0);
  await expect(second.getByLabel('Я готов')).toHaveCount(0);
  const revealedCard = firstColumn.locator('.card').filter({ has: first.getByText('Обновлённый секрет', { exact: true }) });
  await expect(revealedCard).not.toContainText('Второй M3');
  await expect(revealedCard).toContainText('Анонимно');
  const namedCard = secondColumn.locator('.card').filter({
    has: second.getByText('Карточка ведущего', { exact: true }),
  });
  await expect(namedCard).toContainText('Первый M3');

  await Promise.all([firstContext, secondContext, thirdContext].map(closeContextGracefully));
});
