import { expect, Page, test } from '@playwright/test';
import { closeContextGracefully } from './helpers';

async function login(page: Page, baseURL: string, subject: string, displayName: string): Promise<void> {
  await page.goto(`${baseURL}/login`);
  await page.getByLabel('Идентификатор сотрудника').fill(subject);
  await page.getByLabel('Отображаемое имя').fill(displayName);
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Мои встречи' })).toBeVisible();
}

test('M1 templates, meeting, invitation and rotation work in independent contexts', async ({ browser, baseURL }) => {
  const origin = baseURL!;
  const firstContext = await browser.newContext();
  const secondContext = await browser.newContext();
  const thirdContext = await browser.newContext();
  const first = await firstContext.newPage();
  const second = await secondContext.newPage();
  const third = await thirdContext.newPage();

  await login(first, origin, 'employee-a', 'Сотрудник А');
  await login(second, origin, 'employee-b', 'Сотрудник Б');
  await login(third, origin, 'employee-c', 'Сотрудник В');

  await first.goto(`${origin}/templates`);
  await first.getByLabel('Название', { exact: true }).fill('Личный шаблон');
  await first.getByRole('button', { name: 'Создать шаблон' }).click();
  await expect(first.getByText('Шаблон создан.')).toBeVisible();
  await first.getByLabel('Название', { exact: true }).fill('Общий шаблон');
  await first.getByLabel('Видимость').selectOption('shared');
  await first.getByRole('button', { name: 'Создать шаблон' }).click();
  await expect(first.getByText('Шаблон создан.')).toBeVisible();

  await second.goto(`${origin}/templates`);
  await expect(second.getByText('Общий шаблон', { exact: true })).toBeVisible();
  await expect(second.getByText('Личный шаблон', { exact: true })).toHaveCount(0);

  await second.goto(`${origin}/meetings/new`);
  await second.getByLabel('Название встречи').fill('Ретро M1');
  await second.getByRole('button', { name: 'Создать встречу' }).click();
  await expect(second.getByRole('heading', { name: 'Ретро M1', exact: true })).toBeVisible();
  await second.getByLabel('Шаблон колонок').selectOption({ label: 'Общий шаблон' });
  second.once('dialog', (dialog) => dialog.accept());
  await second.getByRole('button', { name: 'Применить', exact: true }).click();
  await second.getByRole('button', { name: 'Сохранить настройки' }).click();
  await expect(second.getByText('Настройки сохранены.')).toBeVisible();

  await first.goto(`${origin}/templates`);
  const sharedCard = first.locator('article').filter({ hasText: 'Общий шаблон' });
  first.once('dialog', (dialog) => dialog.accept());
  await sharedCard.getByRole('button', { name: 'Удалить' }).click();
  await expect(sharedCard).toHaveCount(0);
  await second.reload();
  await expect(second.getByRole('textbox', { name: 'Название колонки' }).nth(0)).toHaveValue('Начать');
  await expect(second.getByRole('textbox', { name: 'Название колонки' }).nth(1)).toHaveValue('Перестать');
  await expect(second.getByRole('textbox', { name: 'Название колонки' }).nth(2)).toHaveValue('Продолжать');

  await second.getByRole('button', { name: 'Показать приглашение' }).click();
  const oldInvite = await second.getByLabel('Ссылка приглашения').inputValue();
  await first.goto(oldInvite);
  await first.getByRole('button', { name: 'Вступить во встречу' }).click();
  await expect(first.getByRole('heading', { name: 'Ретро M1', exact: true })).toBeVisible();
  await expect(first.getByText('Сотрудник А', { exact: false })).toBeVisible();

  await second.reload();
  await second.getByRole('button', { name: 'Показать приглашение' }).click();
  second.once('dialog', (dialog) => dialog.accept());
  await second.getByRole('button', { name: 'Заменить ссылку' }).click();
  await expect(second.getByText('Версия приглашения 2.')).toBeVisible();
  const newInvite = await second.getByLabel('Ссылка приглашения').inputValue();
  expect(newInvite).not.toBe(oldInvite);

  await third.goto(oldInvite);
  await third.getByRole('button', { name: 'Вступить во встречу' }).click();
  await expect(third.getByText('Ссылка приглашения больше не действует.')).toBeVisible();
  await third.goto(newInvite);
  await third.getByRole('button', { name: 'Вступить во встречу' }).click();
  await expect(third.getByRole('heading', { name: 'Ретро M1', exact: true })).toBeVisible();
  await expect(third.getByText('Сотрудник В', { exact: false })).toBeVisible();

  await Promise.all([firstContext, secondContext, thirdContext].map(closeContextGracefully));
});
